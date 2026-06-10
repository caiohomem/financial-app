using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DbUp;
using Ingestion;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<ILlmCategorizationService, LlmCategorizationService>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});
builder.Services.Configure<AnomalyDetectionConfig>(
    builder.Configuration.GetSection(AnomalyDetectionConfig.SectionName));
builder.Services.AddSingleton<AnomalyDetector>();
builder.Services.AddHttpClient<AnomalyExplainer>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
});

var connectionString = builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException(
        "Required configuration 'DATABASE_URL' is missing. Set the DATABASE_URL environment variable before starting the application.");

Program.RunMigrations(connectionString);

var app = builder.Build();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    app.Logger.LogInformation("LLM categorization is disabled because ANTHROPIC_API_KEY is not set.");
}

var parserRegistry = new ParserRegistry();
parserRegistry.Register(".csv", new WiseCsvParser());
parserRegistry.Register(".pdf", new ActivoBankPdfParser());

app.MapGet("/api/health", async () =>
{
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync();

        return Results.Ok(new { status = "ok", db = "reachable" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "degraded", db = "unreachable", error = ex.Message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet(
    "/api/anomalies",
    async (
        string? month,
        IOptions<AnomalyDetectionConfig> configOptions,
        AnomalyDetector detector,
        AnomalyExplainer explainer,
        CancellationToken cancellationToken) =>
    {
        var resolvedMonth = string.IsNullOrWhiteSpace(month)
            ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : month;

        if (!AnomalyDetector.TryParseMonth(resolvedMonth, out _))
        {
            return Results.BadRequest(new { error = "Expected month in YYYY-MM format." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var allTransactions = await Program.LoadTransactionsAsync(connection, cancellationToken);
        var anomalies = detector.Detect(allTransactions, resolvedMonth, configOptions.Value);
        var anomalyPayload = await Program.BuildAnomalyPayloadAsync(anomalies, explainer, cancellationToken);

        return Results.Ok(new
        {
            month = resolvedMonth,
            detectionMethod = AnomalyDetector.DetectionMethod,
            anomalies = anomalyPayload
        });
    });

app.MapPost("/api/imports", async (
    HttpContext context,
    ILlmCategorizationService llmCategorizationService,
    IConfiguration configuration) =>
{
    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    IFormCollection form;
    try
    {
        form = await context.Request.ReadFormAsync();
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }

    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file uploaded." });
    }

    var extension = Path.GetExtension(file.FileName);
    IStatementParser parser;
    string source;
    try
    {
        parser = parserRegistry.Resolve(extension);
        source = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? "wise"
            : "activobank";
    }
    catch (KeyNotFoundException)
    {
        return Results.BadRequest(new
        {
            error = $"Unsupported file type '{extension}'. Expected .csv or .pdf."
        });
    }

    StatementParseResult parsed;
    try
    {
        using var stream = file.OpenReadStream();
        parsed = parser.Parse(stream);
    }
    catch (Exception exception) when (
        exception is InvalidDataException or FormatException or CsvHelper.CsvHelperException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var accountId = await Program.EnsureAccountAsync(
        connection,
        transaction,
        source,
        parsed.AccountIdentifier,
        parsed.Transactions);
    var batchId = await Program.CreateBatchAsync(
        connection,
        transaction,
        source,
        file.FileName,
        parsed.Transactions.Count);
    var summary = await Program.InsertTransactionsAsync(
        connection,
        transaction,
        source,
        accountId,
        batchId,
        parsed.Transactions,
        llmCategorizationService,
        configuration.GetValue<double?>("Categorization:ConfidenceThreshold") ?? 0.85d,
        context.RequestAborted);

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        summary.Imported,
        summary.Ignored,
        byStatus = summary.ByStatus,
        source,
        accountId,
        batchId
    });
});

app.Run();

public partial class Program
{
    internal static void RunMigrations(string connectionString)
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(Program).Assembly,
                script => script.StartsWith("Api.Migrations.", StringComparison.Ordinal))
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw result.Error ?? new InvalidOperationException("Database migration failed.");
        }
    }

    internal static async Task<int> EnsureAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        string identifier,
        IReadOnlyList<ParsedTransaction> transactions)
    {
        var currency = transactions.Select(item => item.Currency)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "EUR";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO accounts (name, source, currency)
            VALUES (@name, @source, @currency)
            ON CONFLICT (source, name) DO UPDATE SET name = EXCLUDED.name
            RETURNING id;
            """;
        command.Parameters.AddWithValue("name", identifier);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("currency", currency);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<int> CreateBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        string filename,
        int rowCount)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO import_batches (source, filename, row_count)
            VALUES (@source, @filename, @rowCount)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("filename", filename);
        command.Parameters.AddWithValue("rowCount", rowCount);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<ImportSummary> InsertTransactionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        int accountId,
        int batchId,
        IReadOnlyList<ParsedTransaction> transactions,
        ILlmCategorizationService llmCategorizationService,
        double confidenceThreshold,
        CancellationToken cancellationToken = default)
    {
        var imported = 0;
        var ignored = 0;
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var evaluator = await CategoryEvaluator.LoadAsync(connection, transaction);
        var canonicalCategories = await LoadCanonicalCategoriesAsync(connection, transaction, cancellationToken);
        var inserts = new List<TransactionInsertCandidate>();
        var pendingLlmInputs = new List<LlmCategorizationInput>();
        var seenWiseTransactionIds = new HashSet<string>(StringComparer.Ordinal);
        var seenDedupHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in transactions)
        {
            var dedupHash = source == "activobank" ? ComputeDedupHash(accountId, item) : null;
            if (source == "wise")
            {
                var sourceTransactionId = item.SourceTransactionId
                    ?? throw new InvalidDataException("Wise transaction is missing its source transaction id.");
                if (!seenWiseTransactionIds.Add(sourceTransactionId))
                {
                    ignored++;
                    continue;
                }
            }
            else if (!seenDedupHashes.Add(dedupHash!))
            {
                ignored++;
                continue;
            }

            if (await TransactionExistsAsync(connection, transaction, source, item, dedupHash))
            {
                ignored++;
                continue;
            }

            var categoryCanonicalId = evaluator.Evaluate(
                item.CategorySource,
                item.NormalizedMerchant,
                item.RawDescription);

            var llmCorrelationId = categoryCanonicalId is null ? inserts.Count : (int?)null;
            if (llmCorrelationId is not null)
            {
                pendingLlmInputs.Add(new LlmCategorizationInput(
                    llmCorrelationId.Value,
                    item.NormalizedMerchant,
                    item.RawDescription));
            }

            inserts.Add(new TransactionInsertCandidate(
                item,
                dedupHash,
                categoryCanonicalId,
                llmCorrelationId));
        }

        var llmResults = pendingLlmInputs.Count == 0
            ? new Dictionary<int, LlmCategorizationResult>()
            : new Dictionary<int, LlmCategorizationResult>(
                await llmCategorizationService.CategorizeAsync(
                    pendingLlmInputs,
                    canonicalCategories.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                    cancellationToken));

        foreach (var insert in inserts)
        {
            var categoryCanonicalId = insert.CategoryCanonicalId;
            LlmCategorizationResult? llmResult = null;
            if (insert.LlmCorrelationId is not null &&
                llmResults.TryGetValue(insert.LlmCorrelationId.Value, out var resolved))
            {
                llmResult = resolved;
                if (resolved.Confidence >= confidenceThreshold &&
                    canonicalCategories.TryGetValue(resolved.Category, out var resolvedCategoryId))
                {
                    categoryCanonicalId = resolvedCategoryId;
                }
            }

            var transactionId = await InsertTransactionAsync(
                connection,
                transaction,
                source,
                accountId,
                batchId,
                insert.Transaction,
                insert.DedupHash,
                categoryCanonicalId);

            if (llmResult?.Suggestion is not null &&
                llmResult.Confidence < confidenceThreshold &&
                canonicalCategories.TryGetValue(llmResult.Category, out var suggestionCategoryId))
            {
                await InsertRuleSuggestionAsync(
                    connection,
                    transaction,
                    transactionId,
                    llmResult.Suggestion,
                    suggestionCategoryId,
                    llmResult.Confidence,
                    cancellationToken);
            }

            imported++;
            byStatus[insert.Transaction.Status] = byStatus.GetValueOrDefault(insert.Transaction.Status) + 1;
        }

        return new ImportSummary(imported, ignored, byStatus);
    }

    internal static string ComputeDedupHash(int accountId, ParsedTransaction transaction)
    {
        var key = string.Join(
            "|",
            accountId.ToString(CultureInfo.InvariantCulture),
            transaction.ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            transaction.Amount.ToString("F4", CultureInfo.InvariantCulture),
            transaction.Direction,
            transaction.RunningBalance?.ToString("F4", CultureInfo.InvariantCulture) ?? string.Empty,
            transaction.RawDescription);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal static async Task<IReadOnlyList<AnomalyDetector.TransactionRow>> LoadTransactionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<AnomalyDetector.TransactionRow>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                transactions.id,
                transactions.raw_description,
                transactions.normalized_merchant,
                transactions.amount,
                transactions.direction,
                categories.name,
                transactions.booking_date
            FROM transactions
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            ORDER BY transactions.booking_date ASC, transactions.id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnomalyDetector.TransactionRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateOnly.FromDateTime(reader.GetDateTime(6))));
        }

        return rows;
    }

    internal static async Task<IReadOnlyList<object?>> BuildAnomalyPayloadAsync(
        IReadOnlyList<AnomalyDetector.AnomalyResult> anomalies,
        AnomalyExplainer explainer,
        CancellationToken cancellationToken)
    {
        var throttler = new SemaphoreSlim(5);
        var tasks = anomalies.Select(async anomaly =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var explanation = await explainer.ExplainAsync(anomaly, cancellationToken);
                return (object?)new
                {
                    transactionId = anomaly.TransactionId,
                    normalizedMerchant = anomaly.NormalizedMerchant,
                    rawDescription = anomaly.RawDescription,
                    amount = anomaly.Amount,
                    direction = anomaly.Direction,
                    category = anomaly.Category,
                    bookingDate = anomaly.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    deviationFactor = Math.Round(anomaly.DeviationFactor, 2),
                    explanation
                };
            }
            finally
            {
                throttler.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private static async Task<bool> TransactionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        ParsedTransaction item,
        string? dedupHash)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (source == "wise")
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM transactions
                    WHERE source = 'wise'
                      AND source_transaction_id = @sourceTransactionId
                );
                """;
            command.Parameters.AddWithValue(
                "sourceTransactionId",
                item.SourceTransactionId
                    ?? throw new InvalidDataException("Wise transaction is missing its source transaction id."));
        }
        else
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM transactions
                    WHERE source = 'activobank'
                      AND dedup_hash = @dedupHash
                );
                """;
            command.Parameters.AddWithValue("dedupHash", dedupHash!);
        }

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> InsertTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        int accountId,
        int batchId,
        ParsedTransaction item,
        string? dedupHash,
        int? categoryCanonicalId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transactions (
                account_id, source, source_transaction_id, booking_date, value_date,
                raw_description, normalized_merchant, amount, direction, currency,
                running_balance, status, category_canonical_id, category_source, import_batch_id, dedup_hash
            )
            VALUES (
                @accountId, @source, @sourceTransactionId, @bookingDate, @valueDate,
                @rawDescription, @normalizedMerchant, @amount, @direction, @currency,
                @runningBalance, @status, @categoryCanonicalId, @categorySource, @batchId, @dedupHash
            )
            RETURNING id;
            """;
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("source", source);
        AddNullable(command, "sourceTransactionId", NpgsqlDbType.Text, item.SourceTransactionId);
        command.Parameters.AddWithValue("bookingDate", NpgsqlDbType.Date, item.BookingDate);
        AddNullable(command, "valueDate", NpgsqlDbType.Date, item.ValueDate);
        command.Parameters.AddWithValue("rawDescription", item.RawDescription);
        AddNullable(command, "normalizedMerchant", NpgsqlDbType.Text, item.NormalizedMerchant);
        command.Parameters.AddWithValue("amount", item.Amount);
        command.Parameters.AddWithValue("direction", item.Direction);
        command.Parameters.AddWithValue("currency", item.Currency);
        AddNullable(command, "runningBalance", NpgsqlDbType.Numeric, item.RunningBalance);
        command.Parameters.AddWithValue("status", item.Status);
        AddNullable(command, "categoryCanonicalId", NpgsqlDbType.Integer, categoryCanonicalId);
        AddNullable(command, "categorySource", NpgsqlDbType.Text, item.CategorySource);
        command.Parameters.AddWithValue("batchId", batchId);
        AddNullable(command, "dedupHash", NpgsqlDbType.Text, dedupHash);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Dictionary<string, int>> LoadCanonicalCategoriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var categories = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name, id
            FROM categories
            ORDER BY name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories[reader.GetString(0)] = reader.GetInt32(1);
        }

        return categories;
    }

    private static async Task InsertRuleSuggestionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int transactionId,
        LlmRuleSuggestion suggestion,
        int categoryCanonicalId,
        double confidence,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rule_suggestions (
                transaction_id,
                suggested_pattern,
                suggested_match_type,
                category_canonical_id,
                confidence
            )
            VALUES (
                @transactionId,
                @suggestedPattern,
                @suggestedMatchType,
                @categoryCanonicalId,
                @confidence
            );
            """;
        command.Parameters.AddWithValue("transactionId", transactionId);
        command.Parameters.AddWithValue("suggestedPattern", suggestion.Pattern);
        command.Parameters.AddWithValue("suggestedMatchType", suggestion.MatchType);
        command.Parameters.AddWithValue("categoryCanonicalId", categoryCanonicalId);
        command.Parameters.AddWithValue("confidence", confidence);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) =>
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);

    internal sealed record ImportSummary(
        int Imported,
        int Ignored,
        IReadOnlyDictionary<string, int> ByStatus);

    private sealed record TransactionInsertCandidate(
        ParsedTransaction Transaction,
        string? DedupHash,
        int? CategoryCanonicalId,
        int? LlmCorrelationId);
}
