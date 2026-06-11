using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DbUp;
using Ingestion;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.Configure<AnomalyDetectionConfig>(
    builder.Configuration.GetSection(AnomalyDetectionConfig.SectionName));
builder.Services.AddSingleton<AnomalyDetector>();
builder.Services.AddHttpClient<AnomalyExplainer>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});
builder.Services.AddHttpClient<ILlmCategorizationService, LlmCategorizationService>();
builder.Services.AddHttpClient<IMonthlyReportService, MonthlyReportService>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});
builder.Services.AddSingleton<RuleManager>();

var connectionString = builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException(
        "Required configuration 'DATABASE_URL' is missing. Set the DATABASE_URL environment variable before starting the application.");

Program.RunMigrations(connectionString);

var app = builder.Build();
app.UseCors();

// Request-level instrumentation: log start, end, status, duration and aborts of every request
app.Use(async (context, next) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    var sw = System.Diagnostics.Stopwatch.StartNew();
    app.Logger.LogInformation(
        "REQ[{RequestId}] {Method} {Path} started (content-length={Length})",
        requestId, context.Request.Method, context.Request.Path,
        context.Request.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "?");
    try
    {
        await next();
        app.Logger.LogInformation(
            "REQ[{RequestId}] {Method} {Path} finished status={Status} in {Elapsed}ms aborted={Aborted}",
            requestId, context.Request.Method, context.Request.Path,
            context.Response.StatusCode, sw.ElapsedMilliseconds,
            context.RequestAborted.IsCancellationRequested);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "REQ[{RequestId}] {Method} {Path} THREW after {Elapsed}ms aborted={Aborted}",
            requestId, context.Request.Method, context.Request.Path,
            sw.ElapsedMilliseconds, context.RequestAborted.IsCancellationRequested);
        throw;
    }
});
var hasAnthropicKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
var hasOpenAiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
if (!hasAnthropicKey && !hasOpenAiKey)
{
    app.Logger.LogInformation("LLM categorization is disabled because neither ANTHROPIC_API_KEY nor OPENAI_API_KEY is set.");
}
else
{
    var provider = hasOpenAiKey ? "OpenAI" : "Anthropic";
    var model = Environment.GetEnvironmentVariable("LLM_MODEL") ?? (hasOpenAiKey ? "gpt-4o-mini" : "claude-haiku-4-5-20251001");
    app.Logger.LogInformation("LLM categorization enabled using {Provider} ({Model}).", provider, model);
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
    "/api/accounts",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var accounts = await Program.LoadDashboardAccountsAsync(connection, cancellationToken);
        return Results.Ok(accounts);
    });

app.MapGet(
    "/api/spending-by-category",
    async (string? month, CancellationToken cancellationToken) =>
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

        var categories = await Program.LoadSpendingByCategoryAsync(connection, resolvedMonth, cancellationToken);
        var totals = await Program.LoadMonthTotalsAsync(connection, resolvedMonth, cancellationToken);
        return Results.Ok(new
        {
            month = resolvedMonth,
            categories,
            totals
        });
    });

app.MapGet(
    "/api/monthly-trend",
    async (string? fromDate, string? toDate, CancellationToken cancellationToken) =>
    {
        DateOnly? parsedFromDate = null;
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            if (!DateOnly.TryParse(fromDate, out var date))
            {
                return Results.BadRequest(new { error = "Expected fromDate in YYYY-MM-DD format." });
            }
            parsedFromDate = date;
        }

        DateOnly? parsedToDate = null;
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            if (!DateOnly.TryParse(toDate, out var date))
            {
                return Results.BadRequest(new { error = "Expected toDate in YYYY-MM-DD format." });
            }
            parsedToDate = date;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var trend = await Program.LoadMonthlyTrendAsync(connection, parsedFromDate, parsedToDate, cancellationToken);
        return Results.Ok(trend);
    });

app.MapGet(
    "/api/transactions",
    async (
        string? month,
        string? fromDate,
        string? toDate,
        string? account,
        string? category,
        string? search,
        CancellationToken cancellationToken) =>
    {
        if (!string.IsNullOrWhiteSpace(month) && !AnomalyDetector.TryParseMonth(month, out _))
        {
            return Results.BadRequest(new { error = "Expected month in YYYY-MM format." });
        }

        DateOnly? parsedFromDate = null;
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            if (!DateOnly.TryParse(fromDate, out var date))
            {
                return Results.BadRequest(new { error = "Expected fromDate in YYYY-MM-DD format." });
            }
            parsedFromDate = date;
        }

        DateOnly? parsedToDate = null;
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            if (!DateOnly.TryParse(toDate, out var date))
            {
                return Results.BadRequest(new { error = "Expected toDate in YYYY-MM-DD format." });
            }
            parsedToDate = date;
        }

        int? accountId = null;
        if (!string.IsNullOrWhiteSpace(account))
        {
            if (!int.TryParse(account, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAccountId))
            {
                return Results.BadRequest(new { error = "Expected account to be a numeric id." });
            }

            accountId = parsedAccountId;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var transactions = await Program.LoadDashboardTransactionsAsync(
            connection,
            month,
            parsedFromDate,
            parsedToDate,
            accountId,
            category,
            search,
            cancellationToken);

        return Results.Ok(transactions);
    });

app.MapGet(
    "/api/review/transactions",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var transactions = await Program.LoadReviewTransactionsAsync(connection, cancellationToken);
        return Results.Ok(transactions);
    });

app.MapGet(
    "/api/review/rule-suggestions",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var suggestions = await Program.LoadPendingRuleSuggestionsAsync(connection, cancellationToken);
        return Results.Ok(suggestions);
    });

app.MapGet(
    "/api/categories",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var categories = await Program.LoadCategoriesAsync(connection, cancellationToken);
        return Results.Ok(categories);
    });

app.MapGet(
    "/api/rules",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rules.id, rules.pattern, rules.match_type, rules.priority,
                   categories.id AS category_id, categories.name AS category_name
            FROM rules
            JOIN categories ON categories.id = rules.category_canonical_id
            ORDER BY rules.priority DESC, rules.pattern ASC;
            """;

        var rules = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new
            {
                id = reader.GetInt32(0),
                pattern = reader.GetString(1),
                matchType = reader.GetString(2),
                priority = reader.GetInt32(3),
                categoryId = reader.GetInt32(4),
                categoryName = reader.GetString(5),
            });
        }

        return Results.Ok(rules);
    });

app.MapPost(
    "/api/rules",
    async (CreateRuleRequest request, RuleManager ruleManager, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            return Results.BadRequest(new { error = "O padrão não pode ser vazio." });
        }

        if (!Program.IsSupportedMatchType(request.MatchType))
        {
            return Results.BadRequest(new { error = "Tipo de correspondência inválido." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await Program.CategoryExistsAsync(connection, transaction, request.CategoryId, cancellationToken))
        {
            return Results.NotFound(new { error = $"Categoria {request.CategoryId} não encontrada." });
        }

        var result = await ruleManager.CreateRuleAndRecategorizeAsync(
            connection, transaction, request.Pattern.Trim(), request.MatchType, request.CategoryId, cancellationToken);

        if (!result.Created)
        {
            return Results.Conflict(new { error = "Já existe uma regra com este padrão e tipo." });
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { recategorizedTransactions = result.RecategorizedTransactions });
    });

app.MapPatch(
    "/api/rules/{id:int}",
    async (int id, UpdateRuleRequest request, RuleManager ruleManager, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            return Results.BadRequest(new { error = "O padrão não pode ser vazio." });
        }

        if (!Program.IsSupportedMatchType(request.MatchType))
        {
            return Results.BadRequest(new { error = "Tipo de correspondência inválido." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await Program.CategoryExistsAsync(connection, transaction, request.CategoryId, cancellationToken))
        {
            return Results.NotFound(new { error = $"Categoria {request.CategoryId} não encontrada." });
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE rules
            SET pattern = @pattern, match_type = @matchType,
                category_canonical_id = @categoryId, priority = @priority
            WHERE id = @id;
            """;
        updateCommand.Parameters.AddWithValue("pattern", request.Pattern.Trim());
        updateCommand.Parameters.AddWithValue("matchType", request.MatchType);
        updateCommand.Parameters.AddWithValue("categoryId", request.CategoryId);
        updateCommand.Parameters.AddWithValue("priority", request.Priority);
        updateCommand.Parameters.AddWithValue("id", id);

        var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            return Results.NotFound(new { error = $"Regra {id} não encontrada." });
        }

        var recategorized = await ruleManager.RecategorizeByRuleAsync(
            connection, transaction, request.Pattern.Trim(), request.MatchType, request.CategoryId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { recategorizedTransactions = recategorized });
    });

app.MapDelete(
    "/api/rules/{id:int}",
    async (int id, CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM rules WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0
            ? Results.NotFound(new { error = $"Regra {id} não encontrada." })
            : Results.Ok();
    });

app.MapPost(
    "/api/categories",
    async (CreateCategoryRequest request, CancellationToken cancellationToken) =>
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "O nome da categoria não pode ser vazio." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories (name, is_system)
            VALUES (@name, false)
            ON CONFLICT DO NOTHING
            RETURNING id, name;
            """;
        command.Parameters.AddWithValue("name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return Results.Ok(new { id = reader.GetInt32(0), name = reader.GetString(1) });
        }

        // Name already exists — return the existing one
        await reader.CloseAsync();
        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = "SELECT id, name FROM categories WHERE name = @name LIMIT 1;";
        selectCommand.Parameters.AddWithValue("name", name);
        await using var selectReader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        await selectReader.ReadAsync(cancellationToken);
        return Results.Ok(new { id = selectReader.GetInt32(0), name = selectReader.GetString(1) });
    });

app.MapPatch(
    "/api/transactions/{id:int}/category",
    async (
        int id,
        UpdateTransactionCategoryRequest request,
        RuleManager ruleManager,
        CancellationToken cancellationToken) =>
    {
        if (!Program.IsSupportedMatchType(request.MatchType))
        {
            return Results.BadRequest(new { error = "Unsupported matchType." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await Program.CategoryExistsAsync(connection, transaction, request.CategoryId, cancellationToken))
        {
            return Results.NotFound(new { error = $"Category {request.CategoryId} was not found." });
        }

        var transactionDetails = await Program.LoadReviewTransactionDetailsAsync(
            connection,
            transaction,
            id,
            cancellationToken);
        if (transactionDetails is null)
        {
            return Results.NotFound(new { error = $"Transaction {id} was not found." });
        }

        await Program.UpdateTransactionCategoryAsync(
            connection,
            transaction,
            id,
            request.CategoryId,
            cancellationToken);
        await Program.RejectPendingSuggestionsForTransactionAsync(
            connection,
            transaction,
            id,
            cancellationToken);

        RuleCreationResult? ruleResult = null;
        if (request.CreateRule)
        {
            var pattern = Program.ResolveRulePattern(request.Pattern, transactionDetails);
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return Results.BadRequest(new { error = "Rule pattern cannot be empty." });
            }

            var matchType = string.IsNullOrWhiteSpace(request.MatchType)
                ? "merchant_eq"
                : request.MatchType.Trim();

            ruleResult = await ruleManager.CreateRuleAndRecategorizeAsync(
                connection,
                transaction,
                pattern,
                matchType,
                request.CategoryId,
                cancellationToken);

            if (!ruleResult.Created)
            {
                return Results.Conflict(new { error = "A rule with the same pattern and match type already exists." });
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new
        {
            transactionId = id,
            categoryId = request.CategoryId,
            ruleCreated = ruleResult?.Created ?? false,
            recategorizedTransactions = ruleResult?.RecategorizedTransactions ?? 0
        });
    });

app.MapPost(
    "/api/review/rule-suggestions/{id:int}/approve",
    async (int id, ApproveSuggestionRequest? request, RuleManager ruleManager, CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var suggestion = await Program.LoadPendingRuleSuggestionForApprovalAsync(
            connection,
            transaction,
            id,
            cancellationToken);
        if (suggestion is null)
        {
            return Results.NotFound(new { error = $"Pending rule suggestion {id} was not found." });
        }

        var pattern = request?.Pattern?.Trim() ?? suggestion.Pattern;
        var matchType = request?.MatchType ?? suggestion.MatchType;
        var categoryId = request?.CategoryId ?? suggestion.CategoryId;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Results.BadRequest(new { error = "O padrão não pode ser vazio." });
        }

        if (!Program.IsSupportedMatchType(matchType))
        {
            return Results.BadRequest(new { error = "Tipo de correspondência inválido." });
        }

        var ruleResult = await ruleManager.CreateRuleAndRecategorizeAsync(
            connection, transaction, pattern, matchType, categoryId, cancellationToken);

        if (!ruleResult.Created)
        {
            return Results.Conflict(new { error = "A rule with the same pattern and match type already exists." });
        }

        await Program.UpdateRuleSuggestionStatusAsync(connection, transaction, id, "approved", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new
        {
            suggestionId = id,
            status = "approved",
            recategorizedTransactions = ruleResult.RecategorizedTransactions
        });
    });

app.MapPost(
    "/api/review/rule-suggestions/{id:int}/reject",
    async (int id, CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await Program.UpdateRuleSuggestionStatusAsync(
            connection,
            transaction,
            id,
            "rejected",
            cancellationToken);
        if (updated == 0)
        {
            return Results.NotFound(new { error = $"Pending rule suggestion {id} was not found." });
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { suggestionId = id, status = "rejected" });
    });

app.MapGet(
    "/api/review/rule-suggestions/{id:int}/preview",
    async (int id, CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var suggestionCommand = connection.CreateCommand();
        suggestionCommand.CommandText = """
            SELECT rs.suggested_pattern, rs.suggested_match_type, rs.transaction_id
            FROM rule_suggestions rs
            WHERE rs.id = @id AND rs.status = 'pending';
            """;
        suggestionCommand.Parameters.AddWithValue("id", id);

        await using var suggestionReader = await suggestionCommand.ExecuteReaderAsync(cancellationToken);
        if (!await suggestionReader.ReadAsync(cancellationToken))
        {
            return Results.NotFound(new { error = "Sugestão não encontrada." });
        }

        var pattern = suggestionReader.GetString(0);
        var matchType = suggestionReader.GetString(1);
        var originTransactionId = suggestionReader.GetInt32(2);
        await suggestionReader.CloseAsync();

        await using var matchCommand = connection.CreateCommand();
        matchCommand.Parameters.AddWithValue("pattern", pattern);

        matchCommand.CommandText = matchType switch
        {
            "merchant_eq" => """
                SELECT id, booking_date, normalized_merchant, raw_description, amount, direction, currency
                FROM transactions
                WHERE normalized_merchant IS NOT NULL AND normalized_merchant ILIKE @pattern
                ORDER BY booking_date DESC LIMIT 50;
                """,
            "contains" => """
                SELECT id, booking_date, normalized_merchant, raw_description, amount, direction, currency
                FROM transactions
                WHERE COALESCE(normalized_merchant, '') ILIKE '%' || @pattern || '%'
                   OR raw_description ILIKE '%' || @pattern || '%'
                ORDER BY booking_date DESC LIMIT 50;
                """,
            _ => """
                SELECT id, booking_date, normalized_merchant, raw_description, amount, direction, currency
                FROM transactions
                WHERE COALESCE(normalized_merchant, raw_description) ~* @pattern
                ORDER BY booking_date DESC LIMIT 50;
                """
        };

        var matches = new List<object>();
        await using var matchReader = await matchCommand.ExecuteReaderAsync(cancellationToken);
        while (await matchReader.ReadAsync(cancellationToken))
        {
            matches.Add(new
            {
                id = matchReader.GetInt32(0),
                bookingDate = DateOnly.FromDateTime(matchReader.GetDateTime(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                normalizedMerchant = matchReader.IsDBNull(2) ? null : matchReader.GetString(2),
                rawDescription = matchReader.GetString(3),
                amount = matchReader.GetDecimal(4),
                direction = matchReader.GetString(5),
                currency = matchReader.GetString(6),
                isOrigin = matchReader.GetInt32(0) == originTransactionId,
            });
        }

        return Results.Ok(new { pattern, matchType, matches });
    });

app.MapGet(
    "/api/review/transfer-candidates",
    async (CancellationToken cancellationToken) =>
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                a.id AS out_id, a.booking_date AS out_date, a.amount,
                a.source AS out_source, oa.name AS out_account,
                a.currency AS out_currency,
                b.id AS in_id, b.booking_date AS in_date,
                b.source AS in_source, ia.name AS in_account,
                b.currency AS in_currency
            FROM transactions a
            JOIN transactions b ON
                a.amount = b.amount
                AND a.direction = 'OUT'
                AND b.direction = 'IN'
                AND a.source <> b.source
                AND ABS(b.booking_date - a.booking_date) <= 3
            JOIN accounts oa ON oa.id = a.account_id
            JOIN accounts ia ON ia.id = b.account_id
            WHERE a.is_own_transfer = false
              AND b.is_own_transfer = false
            ORDER BY a.booking_date DESC, a.amount DESC
            LIMIT 100;
            """;

        var candidates = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new
            {
                outId = reader.GetInt32(0),
                outDate = DateOnly.FromDateTime(reader.GetDateTime(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = reader.GetDecimal(2),
                outSource = reader.GetString(3),
                outAccount = reader.GetString(4),
                outCurrency = reader.GetString(5),
                inId = reader.GetInt32(6),
                inDate = DateOnly.FromDateTime(reader.GetDateTime(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                inSource = reader.GetString(8),
                inAccount = reader.GetString(9),
                inCurrency = reader.GetString(10),
            });
        }

        return Results.Ok(candidates);
    });

app.MapPost(
    "/api/review/transfer-candidates/mark",
    async (MarkTransferRequest request, CancellationToken cancellationToken) =>
    {
        if (request.TransactionIds is null || request.TransactionIds.Count == 0)
        {
            return Results.BadRequest(new { error = "Nenhuma transação especificada." });
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transactions
            SET is_own_transfer = @value
            WHERE id = ANY(@ids);
            """;
        command.Parameters.AddWithValue("value", request.Mark);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer)
        {
            Value = request.TransactionIds.ToArray()
        });

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return Results.Ok(new { markedTransactions = affected });
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

app.MapGet(
    "/api/reports/monthly",
    async (
        string? month,
        IMonthlyReportService reportService,
        IOptions<AnomalyDetectionConfig> anomalyConfig,
        AnomalyDetector anomalyDetector,
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

        var aggregations = await Program.LoadMonthlyAggregationsAsync(connection, resolvedMonth, cancellationToken);
        var allTransactions = await Program.LoadTransactionsAsync(connection, cancellationToken);
        var anomalies = anomalyDetector.Detect(allTransactions, resolvedMonth, anomalyConfig.Value);
        var report = await reportService.GenerateReportAsync(aggregations, anomalies, cancellationToken);

        return Results.Ok(new
        {
            month = resolvedMonth,
            aggregations,
            anomalies,
            report
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

    var cancellationToken = context.RequestAborted;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var importSw = System.Diagnostics.Stopwatch.StartNew();

    StatementParseResult parsed;
    try
    {
        using var stream = file.OpenReadStream();
        parsed = parser.Parse(stream);
    }
    catch (Exception exception) when (
        exception is InvalidDataException or FormatException or CsvHelper.CsvHelperException
            or ArgumentOutOfRangeException)
    {
        logger.LogWarning(exception, "IMPORT[{FileName}] parse failed: {Message}", file.FileName, exception.Message);
        return Results.BadRequest(new { error = exception.Message });
    }

    logger.LogInformation(
        "IMPORT[{FileName}] parsed {Count} transactions (account={Account}) in {Elapsed}ms",
        file.FileName, parsed.Transactions.Count, parsed.AccountIdentifier, importSw.ElapsedMilliseconds);

    var confidenceThreshold = configuration.GetValue<double?>("Categorization:ConfidenceThreshold") ?? 0.85d;

    // Phase 1: read-only pre-flight — check duplicates, evaluate rules, collect LLM inputs.
    // Done outside a transaction so that the long LLM call doesn't hold an open tx.
    IReadOnlyList<TransactionInsertCandidate> inserts;
    IReadOnlyDictionary<string, int> canonicalCategories;
    Dictionary<int, LlmCategorizationResult> llmResults;

    try
    {
        await using var readConn = new NpgsqlConnection(connectionString);
        await readConn.OpenAsync(cancellationToken);

        var evaluator = await CategoryEvaluator.LoadAsync(readConn, null);
        canonicalCategories = await Program.LoadCanonicalCategoriesAsync(readConn, null, cancellationToken);

        var pendingLlmInputs = new List<LlmCategorizationInput>();
        var insertList = new List<TransactionInsertCandidate>();
        var seenWiseIds = new HashSet<string>(StringComparer.Ordinal);
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);

        // We need a temporary accountId just for dedup-hash computation
        // Use a dummy 0 — the real accountId is assigned later in the write transaction
        var tempAccountId = 0;

        foreach (var item in parsed.Transactions)
        {
            var dedupHash = source == "activobank" ? Program.ComputeDedupHash(tempAccountId, item) : null;

            if (source == "wise")
            {
                var srcId = item.SourceTransactionId
                    ?? throw new InvalidDataException("Wise transaction missing source transaction id.");
                if (!seenWiseIds.Add(srcId)) continue;
            }
            else if (!seenHashes.Add(dedupHash!)) continue;

            var categoryCanonicalId = evaluator.Evaluate(
                item.CategorySource,
                item.NormalizedMerchant,
                item.RawDescription);

            var llmCorrelationId = categoryCanonicalId is null ? insertList.Count : (int?)null;
            if (llmCorrelationId is not null)
            {
                pendingLlmInputs.Add(new LlmCategorizationInput(
                    llmCorrelationId.Value,
                    item.NormalizedMerchant,
                    item.RawDescription));
            }

            insertList.Add(new TransactionInsertCandidate(item, dedupHash, categoryCanonicalId, llmCorrelationId));
        }

        logger.LogInformation(
            "IMPORT[{FileName}] pre-flight done: {Inserts} candidates, {LlmInputs} need LLM ({Elapsed}ms)",
            file.FileName, insertList.Count, pendingLlmInputs.Count, importSw.ElapsedMilliseconds);

        // LLM call happens here — connection already closed, no tx held open
        llmResults = pendingLlmInputs.Count == 0
            ? new Dictionary<int, LlmCategorizationResult>()
            : new Dictionary<int, LlmCategorizationResult>(
                await llmCategorizationService.CategorizeAsync(
                    pendingLlmInputs,
                    canonicalCategories.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    cancellationToken));

        logger.LogInformation(
            "IMPORT[{FileName}] LLM phase done: {Results}/{Inputs} categorized ({Elapsed}ms)",
            file.FileName, llmResults.Count, pendingLlmInputs.Count, importSw.ElapsedMilliseconds);

        inserts = insertList;
    }
    catch (OperationCanceledException)
    {
        return Results.Json(new { error = "Request cancelled or timed out." }, statusCode: 499);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Pre-flight failed for file {FileName}", file.FileName);
        return Results.Problem(detail: ex.Message, title: "Import pre-flight failed", statusCode: 500);
    }

    // Phase 2: write transaction — fast, no external calls
    logger.LogInformation("IMPORT[{FileName}] write phase starting ({Elapsed}ms)", file.FileName, importSw.ElapsedMilliseconds);
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

    try
    {
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
        logger.LogInformation(
            "IMPORT[{FileName}] accountId={AccountId} batchId={BatchId}, inserting {Count} candidates",
            file.FileName, accountId, batchId, inserts.Count);
        var summary = await Program.InsertTransactionsWithPrecomputedLlmAsync(
            connection,
            transaction,
            source,
            accountId,
            batchId,
            inserts,
            llmResults,
            canonicalCategories,
            confidenceThreshold,
            cancellationToken,
            logger);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "IMPORT[{FileName}] DONE: imported={Imported} ignored={Ignored} total={Elapsed}ms",
            file.FileName, summary.Imported, summary.Ignored, importSw.ElapsedMilliseconds);

        return Results.Ok(new
        {
            summary.Imported,
            summary.Ignored,
            byStatus = summary.ByStatus,
            source,
            accountId,
            batchId
        });
    }
    catch (OperationCanceledException)
    {
        await transaction.RollbackAsync();
        return Results.Json(new { error = "Request cancelled or timed out." }, statusCode: 499);
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        logger.LogError(ex, "Import write phase failed for file {FileName}", file.FileName);
        return Results.Problem(detail: ex.Message, title: "Import failed", statusCode: 500);
    }
});

app.MapPatch("/api/transactions/{id}", async (int id, UpdateTransactionCategoryRequest request, CancellationToken cancellationToken) =>
{
    if (request.CategoryId == 0) return Results.BadRequest(new { error = "CategoryId é obrigatório." });

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();
    command.CommandText = """
        UPDATE transactions SET category_canonical_id = @catId WHERE id = @id;
        """;
    command.Parameters.AddWithValue("catId", request.CategoryId);
    command.Parameters.AddWithValue("id", id);
    var updated = await command.ExecuteNonQueryAsync(cancellationToken);

    if (updated == 0) return Results.NotFound();

    return Results.Ok(new { message = "Categoria atualizada.", transactionId = id, categoryId = request.CategoryId });
});

app.MapPost("/api/debug/reset", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        TRUNCATE rule_suggestions, transactions, import_batches, accounts RESTART IDENTITY CASCADE;
        """;
    await command.ExecuteNonQueryAsync();

    return Results.Ok(new { message = "Base de dados limpa com sucesso." });
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

        // Create any new categories the LLM proposed, deduplicating against existing ones (ILIKE)
        var proposedNames = llmResults.Values
            .Where(r => r.NewCategoryName is not null)
            .Select(r => r.NewCategoryName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var proposed in proposedNames)
        {
            if (canonicalCategories.Keys.Any(k => string.Equals(k, proposed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            await using var catCmd = connection.CreateCommand();
            catCmd.Transaction = transaction;
            catCmd.CommandText = """
                INSERT INTO categories (name, is_system)
                VALUES (@name, false)
                ON CONFLICT DO NOTHING
                RETURNING id, name;
                """;
            catCmd.Parameters.AddWithValue("name", proposed);

            await using var catReader = await catCmd.ExecuteReaderAsync(cancellationToken);
            if (await catReader.ReadAsync(cancellationToken))
            {
                canonicalCategories[catReader.GetString(1)] = catReader.GetInt32(0);
            }
            else
            {
                await catReader.CloseAsync();
                await using var selectCmd = connection.CreateCommand();
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT id, name FROM categories WHERE name ILIKE @name LIMIT 1;";
                selectCmd.Parameters.AddWithValue("name", proposed);
                await using var selReader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                if (await selReader.ReadAsync(cancellationToken))
                {
                    canonicalCategories[selReader.GetString(1)] = selReader.GetInt32(0);
                }
            }
        }

        foreach (var insert in inserts)
        {
            var categoryCanonicalId = insert.CategoryCanonicalId;
            LlmCategorizationResult? llmResult = null;
            if (insert.LlmCorrelationId is not null &&
                llmResults.TryGetValue(insert.LlmCorrelationId.Value, out var resolved))
            {
                llmResult = resolved;

                // Use an existing category if confidence is high enough
                if (resolved.Confidence >= confidenceThreshold &&
                    resolved.Category is not null &&
                    canonicalCategories.TryGetValue(resolved.Category, out var resolvedCategoryId))
                {
                    categoryCanonicalId = resolvedCategoryId;
                }
                // Use a newly-created category when LLM proposed one and it now exists
                else if (resolved.NewCategoryName is not null &&
                    canonicalCategories.TryGetValue(resolved.NewCategoryName, out var newCatId))
                {
                    categoryCanonicalId = newCatId;
                }
                else if (resolved.NewCategoryName is not null)
                {
                    // Try case-insensitive lookup in case key casing differs
                    var match = canonicalCategories.Keys.FirstOrDefault(
                        k => string.Equals(k, resolved.NewCategoryName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        categoryCanonicalId = canonicalCategories[match];
                    }
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

            var suggestionCategoryKey = llmResult?.Category
                ?? llmResult?.NewCategoryName;
            if (llmResult?.Suggestion is not null &&
                llmResult.Confidence < confidenceThreshold &&
                suggestionCategoryKey is not null &&
                canonicalCategories.TryGetValue(suggestionCategoryKey, out var suggestionCategoryId))
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

    internal static async Task<ImportSummary> InsertTransactionsWithPrecomputedLlmAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        int accountId,
        int batchId,
        IReadOnlyList<TransactionInsertCandidate> inserts,
        IReadOnlyDictionary<int, LlmCategorizationResult> llmResults,
        IReadOnlyDictionary<string, int> canonicalCategoriesReadOnly,
        double confidenceThreshold,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var imported = 0;
        var ignored = 0;
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Get up-to-date categories including any new ones added by LLM phase
        var canonicalCategories = await Program.LoadCanonicalCategoriesAsync(connection, transaction, cancellationToken);

        // Create any new categories the LLM proposed
        var proposedNames = llmResults.Values
            .Where(r => r.NewCategoryName is not null)
            .Select(r => r.NewCategoryName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (proposedNames.Count > 0)
        {
            logger?.LogInformation(
                "PERSIST[batch={BatchId}] LLM proposed {Count} new categories: [{Names}]",
                batchId, proposedNames.Count, string.Join(", ", proposedNames));
        }

        foreach (var proposed in proposedNames)
        {
            if (canonicalCategories.Keys.Any(k => string.Equals(k, proposed, StringComparison.OrdinalIgnoreCase)))
                continue;

            await using var catCmd = connection.CreateCommand();
            catCmd.Transaction = transaction;
            catCmd.CommandText = """
                INSERT INTO categories (name, is_system)
                VALUES (@name, false)
                ON CONFLICT DO NOTHING
                RETURNING id, name;
                """;
            catCmd.Parameters.AddWithValue("name", proposed);

            await using var catReader = await catCmd.ExecuteReaderAsync(cancellationToken);
            if (await catReader.ReadAsync(cancellationToken))
            {
                canonicalCategories[catReader.GetString(1)] = catReader.GetInt32(0);
            }
            else
            {
                await catReader.CloseAsync();
                await using var selectCmd = connection.CreateCommand();
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT id, name FROM categories WHERE name ILIKE @name LIMIT 1;";
                selectCmd.Parameters.AddWithValue("name", proposed);
                await using var selReader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                if (await selReader.ReadAsync(cancellationToken))
                    canonicalCategories[selReader.GetString(1)] = selReader.GetInt32(0);
            }
        }

        // Recompute dedup hashes using the real accountId
        var seenWiseIds = new HashSet<string>(StringComparer.Ordinal);
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var insert in inserts)
        {
            // Re-check duplicates with real accountId
            var realHash = source == "activobank" ? Program.ComputeDedupHash(accountId, insert.Transaction) : null;

            if (source == "wise")
            {
                var srcId = insert.Transaction.SourceTransactionId ?? string.Empty;
                if (!seenWiseIds.Add(srcId)) { ignored++; continue; }
            }
            else if (!seenHashes.Add(realHash!)) { ignored++; continue; }

            if (await TransactionExistsAsync(connection, transaction, source, insert.Transaction, realHash))
            {
                ignored++;
                continue;
            }

            var categoryCanonicalId = insert.CategoryCanonicalId;
            LlmCategorizationResult? llmResult = null;

            if (insert.LlmCorrelationId is not null &&
                llmResults.TryGetValue(insert.LlmCorrelationId.Value, out var resolved))
            {
                llmResult = resolved;
                if (resolved.Confidence >= confidenceThreshold &&
                    resolved.Category is not null &&
                    canonicalCategories.TryGetValue(resolved.Category, out var resolvedCategoryId))
                {
                    categoryCanonicalId = resolvedCategoryId;
                }
                else if (resolved.NewCategoryName is not null &&
                    canonicalCategories.TryGetValue(resolved.NewCategoryName, out var newCatId))
                {
                    categoryCanonicalId = newCatId;
                }
                else if (resolved.NewCategoryName is not null)
                {
                    var match = canonicalCategories.Keys.FirstOrDefault(
                        k => string.Equals(k, resolved.NewCategoryName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        categoryCanonicalId = canonicalCategories[match];
                }
            }

            var transactionId = await InsertTransactionAsync(
                connection, transaction, source, accountId, batchId,
                insert.Transaction, realHash, categoryCanonicalId);

            var suggestionCategoryKey = llmResult?.Category ?? llmResult?.NewCategoryName;
            if (llmResult?.Suggestion is not null &&
                llmResult.Confidence < confidenceThreshold &&
                suggestionCategoryKey is not null &&
                canonicalCategories.TryGetValue(suggestionCategoryKey, out var suggestionCategoryId))
            {
                await InsertRuleSuggestionAsync(
                    connection, transaction, transactionId,
                    llmResult.Suggestion, suggestionCategoryId,
                    llmResult.Confidence, cancellationToken);
            }

            imported++;
            byStatus[insert.Transaction.Status] = byStatus.GetValueOrDefault(insert.Transaction.Status) + 1;
        }

        logger?.LogInformation(
            "PERSIST[batch={BatchId}] insert loop finished: imported={Imported} ignored={Ignored}",
            batchId, imported, ignored);

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

    internal static async Task<MonthlyAggregations> LoadMonthlyAggregationsAsync(
        NpgsqlConnection connection,
        string month,
        CancellationToken cancellationToken)
    {
        if (!AnomalyDetector.TryParseMonth(month, out var monthStart))
        {
            throw new ArgumentException("Expected month in YYYY-MM format.", nameof(month));
        }

        var currentFrom = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var currentTo = monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var priorFrom = monthStart.AddMonths(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        decimal totalOut = 0m;
        decimal totalIn = 0m;
        var transactionCount = 0;
        decimal? priorMonthTotalOut = null;

        await using (var totalsCommand = connection.CreateCommand())
        {
            totalsCommand.CommandText = """
                SELECT
                    direction,
                    COALESCE(SUM(amount) FILTER (
                        WHERE booking_date >= @currentFrom AND booking_date < @currentTo
                    ), 0) AS current_total,
                    COALESCE(SUM(amount) FILTER (
                        WHERE booking_date >= @priorFrom AND booking_date < @currentFrom
                    ), 0) AS prior_total,
                    COUNT(*) FILTER (
                        WHERE booking_date >= @currentFrom AND booking_date < @currentTo
                    ) AS current_count
                FROM transactions
                WHERE status <> 'cancelled'
                  AND is_own_transfer = false
                  AND booking_date >= @priorFrom
                  AND booking_date < @currentTo
                GROUP BY direction;
                """;
            totalsCommand.Parameters.AddWithValue("currentFrom", currentFrom);
            totalsCommand.Parameters.AddWithValue("currentTo", currentTo);
            totalsCommand.Parameters.AddWithValue("priorFrom", priorFrom);

            await using var reader = await totalsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var direction = reader.GetString(0);
                var currentTotal = reader.GetDecimal(1);
                var priorTotal = reader.GetDecimal(2);
                var currentCount = reader.GetInt64(3);

                if (string.Equals(direction, "OUT", StringComparison.OrdinalIgnoreCase))
                {
                    totalOut = currentTotal;
                    priorMonthTotalOut = priorTotal;
                }
                else if (string.Equals(direction, "IN", StringComparison.OrdinalIgnoreCase))
                {
                    totalIn = currentTotal;
                }

                transactionCount += Convert.ToInt32(currentCount, CultureInfo.InvariantCulture);
            }
        }

        var topCategories = new List<MonthlyCategorySummary>();
        await using (var categoriesCommand = connection.CreateCommand())
        {
            categoriesCommand.CommandText = """
                SELECT
                    COALESCE(categories.name, 'Sem categoria') AS name,
                    COALESCE(SUM(transactions.amount), 0) AS total_out,
                    COUNT(*) AS item_count
                FROM transactions
                LEFT JOIN categories ON categories.id = transactions.category_canonical_id
                WHERE transactions.status <> 'cancelled'
                  AND transactions.direction = 'OUT'
                  AND transactions.is_own_transfer = false
                  AND transactions.booking_date >= @currentFrom
                  AND transactions.booking_date < @currentTo
                GROUP BY COALESCE(categories.name, 'Sem categoria')
                ORDER BY total_out DESC, name ASC
                LIMIT 5;
                """;
            categoriesCommand.Parameters.AddWithValue("currentFrom", currentFrom);
            categoriesCommand.Parameters.AddWithValue("currentTo", currentTo);

            await using var reader = await categoriesCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                topCategories.Add(new MonthlyCategorySummary(
                    reader.GetString(0),
                    reader.GetDecimal(1),
                    Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture)));
            }
        }

        var topMerchants = new List<MonthlyMerchantSummary>();
        await using (var merchantsCommand = connection.CreateCommand())
        {
            merchantsCommand.CommandText = """
                SELECT
                    COALESCE(NULLIF(normalized_merchant, ''), raw_description) AS merchant_name,
                    COALESCE(SUM(amount), 0) AS total_out,
                    COUNT(*) AS item_count
                FROM transactions
                WHERE status <> 'cancelled'
                  AND direction = 'OUT'
                  AND is_own_transfer = false
                  AND booking_date >= @currentFrom
                  AND booking_date < @currentTo
                GROUP BY COALESCE(NULLIF(normalized_merchant, ''), raw_description)
                ORDER BY total_out DESC, merchant_name ASC
                LIMIT 5;
                """;
            merchantsCommand.Parameters.AddWithValue("currentFrom", currentFrom);
            merchantsCommand.Parameters.AddWithValue("currentTo", currentTo);

            await using var reader = await merchantsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                topMerchants.Add(new MonthlyMerchantSummary(
                    reader.GetString(0),
                    reader.GetDecimal(1),
                    Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture)));
            }
        }

        return new MonthlyAggregations(
            month,
            totalOut,
            totalIn,
            transactionCount,
            priorMonthTotalOut,
            topCategories,
            topMerchants);
    }

    internal static async Task<IReadOnlyList<DashboardAccountBalance>> LoadDashboardAccountsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var accounts = new List<DashboardAccountBalance>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                accounts.id,
                accounts.name,
                accounts.source,
                accounts.currency,
                COALESCE(SUM(CASE WHEN transactions.direction = 'IN' AND transactions.status <> 'cancelled' THEN transactions.amount ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN transactions.direction = 'OUT' AND transactions.status <> 'cancelled' THEN transactions.amount ELSE 0 END), 0)
                    AS balance
            FROM accounts
            LEFT JOIN transactions ON transactions.account_id = accounts.id
            GROUP BY accounts.id, accounts.name, accounts.source, accounts.currency
            ORDER BY accounts.name ASC, accounts.id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new DashboardAccountBalance(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4)));
        }

        return accounts;
    }

    internal static async Task<IReadOnlyList<MonthlyTrendItem>> LoadMonthlyTrendAsync(
        NpgsqlConnection connection,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var trends = new List<MonthlyTrendItem>();

        await using var command = connection.CreateCommand();
        var conditions = new List<string>
        {
            "transactions.status <> 'cancelled'",
            "transactions.is_own_transfer = false"
        };

        if (fromDate is not null)
        {
            conditions.Add("transactions.booking_date >= @fromDate");
            command.Parameters.AddWithValue("fromDate", fromDate.Value);
        }

        if (toDate is not null)
        {
            conditions.Add("transactions.booking_date <= @toDate");
            command.Parameters.AddWithValue("toDate", toDate.Value);
        }

        // Without an explicit range, keep the original behaviour: last 12 months
        var limitClause = fromDate is null && toDate is null ? "LIMIT 12" : string.Empty;

        command.CommandText = $"""
            SELECT
                TO_CHAR(transactions.booking_date, 'YYYY-MM') AS month,
                SUM(CASE WHEN transactions.direction = 'IN' THEN transactions.amount ELSE 0 END) AS credits,
                SUM(CASE WHEN transactions.direction = 'OUT' THEN ABS(transactions.amount) ELSE 0 END) AS debits
            FROM transactions
            WHERE {string.Join(" AND ", conditions)}
            GROUP BY TO_CHAR(transactions.booking_date, 'YYYY-MM')
            ORDER BY month DESC
            {limitClause};
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trends.Add(new MonthlyTrendItem(
                reader.GetString(0),
                Math.Round(reader.GetDecimal(1), 2),
                Math.Round(reader.GetDecimal(2), 2)));
        }

        trends.Reverse();
        return trends;
    }

    internal static async Task<MonthTotals> LoadMonthTotalsAsync(
        NpgsqlConnection connection,
        string month,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN transactions.direction = 'IN' THEN transactions.amount ELSE 0 END), 0) AS credits,
                COALESCE(SUM(CASE WHEN transactions.direction = 'OUT' THEN ABS(transactions.amount) ELSE 0 END), 0) AS debits
            FROM transactions
            WHERE transactions.status <> 'cancelled'
              AND transactions.is_own_transfer = false
              AND TO_CHAR(transactions.booking_date, 'YYYY-MM') = @month;
            """;
        command.Parameters.AddWithValue("month", month);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new MonthTotals(
                Math.Round(reader.GetDecimal(0), 2),
                Math.Round(reader.GetDecimal(1), 2));
        }

        return new MonthTotals(0, 0);
    }

    internal static async Task<IReadOnlyList<DashboardCategorySpend>> LoadSpendingByCategoryAsync(
        NpgsqlConnection connection,
        string month,
        CancellationToken cancellationToken)
    {
        var categories = new List<DashboardCategorySpend>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(categories.name, 'Uncategorized') AS category,
                COALESCE(SUM(transactions.amount), 0) AS total
            FROM transactions
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE transactions.direction = 'OUT'
              AND transactions.status <> 'cancelled'
              AND transactions.is_own_transfer = false
              AND TO_CHAR(transactions.booking_date, 'YYYY-MM') = @month
            GROUP BY COALESCE(categories.name, 'Uncategorized')
            ORDER BY total DESC, category ASC;
            """;
        command.Parameters.AddWithValue("month", month);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new DashboardCategorySpend(
                reader.GetString(0),
                reader.GetDecimal(1)));
        }

        return categories;
    }

    internal static async Task<IReadOnlyList<DashboardTransactionItem>> LoadDashboardTransactionsAsync(
        NpgsqlConnection connection,
        string? month,
        DateOnly? fromDate,
        DateOnly? toDate,
        int? accountId,
        string? category,
        string? search,
        CancellationToken cancellationToken)
    {
        var transactions = new List<DashboardTransactionItem>();
        var conditions = new List<string>();

        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                transactions.id,
                transactions.booking_date,
                transactions.normalized_merchant,
                transactions.raw_description,
                transactions.amount,
                transactions.direction,
                transactions.currency,
                transactions.status,
                categories.name AS category_name,
                accounts.name AS account_name,
                transactions.source
            FROM transactions
            JOIN accounts ON accounts.id = transactions.account_id
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            """);

        if (!string.IsNullOrWhiteSpace(month))
        {
            conditions.Add("TO_CHAR(transactions.booking_date, 'YYYY-MM') = @month");
            command.Parameters.AddWithValue("month", month);
        }

        if (fromDate is not null)
        {
            conditions.Add("transactions.booking_date >= @fromDate");
            command.Parameters.AddWithValue("fromDate", fromDate.Value.ToDateTime(TimeOnly.MinValue));
        }

        if (toDate is not null)
        {
            conditions.Add("transactions.booking_date <= @toDate");
            command.Parameters.AddWithValue("toDate", toDate.Value.ToDateTime(TimeOnly.MaxValue));
        }

        if (accountId is not null)
        {
            conditions.Add("transactions.account_id = @accountId");
            command.Parameters.AddWithValue("accountId", accountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            conditions.Add("COALESCE(categories.name, 'Uncategorized') = @category");
            command.Parameters.AddWithValue("category", category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditions.Add("""
                (
                    COALESCE(transactions.normalized_merchant, '') ILIKE @search
                    OR transactions.raw_description ILIKE @search
                )
                """);
            command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
        }

        if (conditions.Count > 0)
        {
            sql.AppendLine();
            sql.Append("WHERE ");
            sql.Append(string.Join(" AND ", conditions));
        }

        sql.AppendLine();
        sql.Append("""
            ORDER BY transactions.booking_date DESC, transactions.id DESC;
            """);

        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bookingDate = DateOnly.FromDateTime(reader.GetDateTime(1));
            var normalizedMerchant = reader.IsDBNull(2) ? null : reader.GetString(2);
            transactions.Add(new DashboardTransactionItem(
                reader.GetInt32(0),
                bookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(normalizedMerchant) ? reader.GetString(3) : normalizedMerchant,
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? "Uncategorized" : reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return transactions;
    }

    internal static async Task<IReadOnlyList<ReviewTransactionItem>> LoadReviewTransactionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var transactions = new List<ReviewTransactionItem>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                transactions.id,
                transactions.booking_date,
                transactions.normalized_merchant,
                transactions.raw_description,
                transactions.amount,
                transactions.account_id,
                transactions.category_canonical_id,
                categories.name AS category_name,
                transactions.currency
            FROM transactions
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE transactions.category_canonical_id IS NULL
               OR EXISTS (
                   SELECT 1
                   FROM rule_suggestions
                   WHERE rule_suggestions.transaction_id = transactions.id
                     AND rule_suggestions.status = 'pending'
               )
            ORDER BY transactions.booking_date DESC, transactions.id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transactions.Add(new ReviewTransactionItem(
                reader.GetInt32(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }

        return transactions;
    }

    internal static async Task<IReadOnlyList<RuleSuggestionItem>> LoadPendingRuleSuggestionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RuleSuggestionItem>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                rule_suggestions.id,
                rule_suggestions.transaction_id,
                transactions.normalized_merchant,
                rule_suggestions.suggested_pattern,
                rule_suggestions.suggested_match_type,
                rule_suggestions.category_canonical_id,
                categories.name,
                rule_suggestions.confidence
            FROM rule_suggestions
            JOIN transactions ON transactions.id = rule_suggestions.transaction_id
            JOIN categories ON categories.id = rule_suggestions.category_canonical_id
            WHERE rule_suggestions.status = 'pending'
            ORDER BY rule_suggestions.created_at DESC, rule_suggestions.id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(new RuleSuggestionItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetDouble(7)));
        }

        return suggestions;
    }

    internal static async Task<IReadOnlyList<CategoryItem>> LoadCategoriesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var categories = new List<CategoryItem>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name
            FROM categories
            ORDER BY name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new CategoryItem(reader.GetInt32(0), reader.GetString(1)));
        }

        return categories;
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

    private static async Task<bool> CategoryExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int categoryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM categories
                WHERE id = @categoryId
            );
            """;
        command.Parameters.AddWithValue("categoryId", categoryId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<ReviewTransactionDetails?> LoadReviewTransactionDetailsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT normalized_merchant, raw_description
            FROM transactions
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReviewTransactionDetails(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetString(1));
    }

    private static async Task UpdateTransactionCategoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int id,
        int categoryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transactions
            SET category_canonical_id = @categoryId
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("categoryId", categoryId);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PendingRuleSuggestionApproval?> LoadPendingRuleSuggestionForApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT suggested_pattern, suggested_match_type, category_canonical_id
            FROM rule_suggestions
            WHERE id = @id
              AND status = 'pending';
            """;
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PendingRuleSuggestionApproval(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2));
    }

    private static async Task<int> UpdateRuleSuggestionStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int id,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE rule_suggestions
            SET status = @status
            WHERE id = @id
              AND status = 'pending';
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RejectPendingSuggestionsForTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int transactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE rule_suggestions
            SET status = 'rejected'
            WHERE transaction_id = @transactionId
              AND status = 'pending';
            """;
        command.Parameters.AddWithValue("transactionId", transactionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    internal static async Task<Dictionary<string, int>> LoadCanonicalCategoriesAsync(
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

    private static string ResolveRulePattern(string? requestedPattern, ReviewTransactionDetails transaction) =>
        string.IsNullOrWhiteSpace(requestedPattern)
            ? transaction.NormalizedMerchant ?? transaction.RawDescription
            : requestedPattern.Trim();

    private static bool IsSupportedMatchType(string? matchType) =>
        string.IsNullOrWhiteSpace(matchType) ||
        matchType is "merchant_eq" or "contains" or "regex";

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

    internal sealed record ReviewTransactionItem(
        int Id,
        string Date,
        string? NormalizedMerchant,
        string RawDescription,
        decimal Amount,
        int AccountId,
        int? CategoryCanonicalId,
        string? CategoryName,
        string Currency);

    internal sealed record RuleSuggestionItem(
        int Id,
        int TransactionId,
        string? NormalizedMerchant,
        string SuggestedPattern,
        string SuggestedMatchType,
        int CategoryCanonicalId,
        string CategoryName,
        double Confidence);

    internal sealed record CategoryItem(int Id, string Name);

    internal sealed record ApproveSuggestionRequest(string? Pattern, string? MatchType, int? CategoryId);

    internal sealed record MarkTransferRequest(IReadOnlyList<int>? TransactionIds, bool Mark);

    internal sealed record CreateCategoryRequest(string? Name);

    internal sealed record CreateRuleRequest(string? Pattern, string MatchType, int CategoryId);

    internal sealed record UpdateRuleRequest(string? Pattern, string MatchType, int CategoryId, int Priority);

    internal sealed record UpdateTransactionCategoryRequest(
        int CategoryId,
        bool CreateRule,
        string? MatchType,
        string? Pattern);

    internal sealed record DashboardAccountBalance(
        int Id,
        string Name,
        string Source,
        string Currency,
        decimal Balance);

    internal sealed record DashboardCategorySpend(
        string Category,
        decimal Total);

    internal sealed record MonthlyTrendItem(
        string Month,
        decimal Credits,
        decimal Debits);

    internal sealed record MonthTotals(
        decimal Credits,
        decimal Debits);

    internal sealed record DashboardTransactionItem(
        int Id,
        string BookingDate,
        string NormalizedMerchant,
        string RawDescription,
        decimal Amount,
        string Direction,
        string Currency,
        string Status,
        string Category,
        string AccountName,
        string Source);

    internal sealed record TransactionInsertCandidate(
        ParsedTransaction Transaction,
        string? DedupHash,
        int? CategoryCanonicalId,
        int? LlmCorrelationId);

    private sealed record ReviewTransactionDetails(string? NormalizedMerchant, string RawDescription);

    private sealed record PendingRuleSuggestionApproval(string Pattern, string MatchType, int CategoryId);
}
