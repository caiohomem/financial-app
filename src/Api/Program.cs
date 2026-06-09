using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DbUp;
using Ingestion;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException(
        "Required configuration 'DATABASE_URL' is missing. Set the DATABASE_URL environment variable before starting the application.");

Program.RunMigrations(connectionString);

var app = builder.Build();
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

app.MapPost("/api/imports", async (HttpContext context) =>
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
        parsed.Transactions);

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
        IReadOnlyList<ParsedTransaction> transactions)
    {
        var imported = 0;
        var ignored = 0;
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in transactions)
        {
            var dedupHash = source == "activobank" ? ComputeDedupHash(accountId, item) : null;
            if (await TransactionExistsAsync(connection, transaction, source, item, dedupHash))
            {
                ignored++;
                continue;
            }

            await InsertTransactionAsync(
                connection,
                transaction,
                source,
                accountId,
                batchId,
                item,
                dedupHash);
            imported++;
            byStatus[item.Status] = byStatus.GetValueOrDefault(item.Status) + 1;
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

    private static async Task InsertTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        int accountId,
        int batchId,
        ParsedTransaction item,
        string? dedupHash)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transactions (
                account_id, source, source_transaction_id, booking_date, value_date,
                raw_description, normalized_merchant, amount, direction, currency,
                running_balance, status, category_source, import_batch_id, dedup_hash
            )
            VALUES (
                @accountId, @source, @sourceTransactionId, @bookingDate, @valueDate,
                @rawDescription, @normalizedMerchant, @amount, @direction, @currency,
                @runningBalance, @status, @categorySource, @batchId, @dedupHash
            );
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
        AddNullable(command, "categorySource", NpgsqlDbType.Text, item.CategorySource);
        command.Parameters.AddWithValue("batchId", batchId);
        AddNullable(command, "dedupHash", NpgsqlDbType.Text, dedupHash);

        await command.ExecuteNonQueryAsync();
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
}
