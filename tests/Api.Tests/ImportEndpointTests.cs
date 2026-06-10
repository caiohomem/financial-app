using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class ImportEndpointTests
{
    [Fact]
    public async Task Import_WiseAndActivoBank_IsIdempotentAndCreatesAccounts()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await CleanImportData(databaseUrl);

        var wiseFirst = await Upload(client, "wise_sample.csv");
        var wiseSecond = await Upload(client, "wise_sample.csv");
        var activoFirst = await Upload(client, "activo_2026_005.pdf");
        var activoSecond = await Upload(client, "activo_2026_005.pdf");

        Assert.True(wiseFirst.Imported > 0);
        Assert.Equal(0, wiseSecond.Imported);
        Assert.Equal(wiseFirst.Imported, wiseSecond.Ignored);
        Assert.True(activoFirst.Imported > 0);
        Assert.Equal(0, activoSecond.Imported);
        Assert.Equal(activoFirst.Imported, activoSecond.Ignored);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        Assert.Equal(2, await ScalarInt(connection, "SELECT count(*) FROM accounts;"));
        Assert.Equal(4, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'activobank'
              AND raw_description LIKE '%A.M.O.BREWERY%'
              AND booking_date = DATE '2026-05-19'
              AND amount = 3.50;
            """));
        Assert.Equal(activoFirst.Imported, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'activobank'
              AND normalized_merchant IS NOT NULL
              AND normalized_merchant <> raw_description;
            """));
        Assert.Equal(wiseFirst.Imported, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'wise'
              AND normalized_merchant IS NULL;
            """));
        Assert.Equal(0, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'wise'
              AND category_source IS NOT NULL
              AND category_canonical_id IS NULL;
            """));
        Assert.Equal(0, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM (
                VALUES
                    ('Glovo'),
                    ('Uber Eats'),
                    ('Bolt'),
                    ('Metropolitano De Lisboa'),
                    ('Generali'),
                    ('Endesa'),
                    ('Vodafone'),
                    ('Cetelem'),
                    ('A.M.O.Brewery'),
                    ('Cerveja Canil')
            ) AS known(pattern)
            JOIN transactions ON transactions.source = 'activobank'
            WHERE transactions.normalized_merchant ILIKE '%' || known.pattern || '%'
              AND transactions.category_canonical_id IS NULL;
            """));
        Assert.Equal(activoFirst.Imported, await ScalarInt(
            connection,
            "SELECT count(*) FROM transactions WHERE source = 'activobank';"));
    }

    [Fact]
    public async Task Import_RejectsMissingAndUnsupportedFiles()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var missing = new MultipartFormDataContent();
        var missingResponse = await client.PostAsync("/api/imports", missing);

        using var unsupported = new MultipartFormDataContent();
        unsupported.Add(new StringContent("content"), "file", "statement.txt");
        var unsupportedResponse = await client.PostAsync("/api/imports", unsupported);

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
    }

    [Fact]
    public async Task Import_AutoAppliesLlmCategory_WhenConfidenceMeetsDefaultThreshold()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory(
            new StubLlmCategorizationService((transactions, _) => new Dictionary<int, LlmCategorizationResult>
            {
                [transactions[0].TransactionId] = new(
                    transactions[0].TransactionId,
                    "Restaurantes",
                    0.90,
                    null)
            }));
        using var client = factory.CreateClient();
        await CleanImportData(databaseUrl);

        var response = await UploadWiseCsv(
            client,
            transferNumber: "llm-auto-apply",
            beneficiaryName: "Coffee Lab Lisbon");

        Assert.Equal(1, response.Imported);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE transactions.source = 'wise'
              AND transactions.source_transaction_id = 'llm-auto-apply'
              AND categories.name = 'Restaurantes';
            """));
        Assert.Equal(0, await ScalarInt(connection, "SELECT count(*) FROM rule_suggestions;"));
    }

    [Fact]
    public async Task Import_LeavesTransactionForReview_WhenConfidenceIsBelowConfiguredThreshold()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory(
            new StubLlmCategorizationService((transactions, _) => new Dictionary<int, LlmCategorizationResult>
            {
                [transactions[0].TransactionId] = new(
                    transactions[0].TransactionId,
                    "Restaurantes",
                    0.80,
                    new LlmRuleSuggestion("Coffee Lab", "merchant_eq"))
            }));
        using var client = factory.CreateClient();
        await CleanImportData(databaseUrl);

        var response = await UploadWiseCsv(
            client,
            transferNumber: "llm-review",
            beneficiaryName: "Coffee Lab Lisbon");

        Assert.Equal(1, response.Imported);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'wise'
              AND source_transaction_id = 'llm-review'
              AND category_canonical_id IS NULL;
            """));
        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rule_suggestions suggestions
            JOIN transactions ON transactions.id = suggestions.transaction_id
            JOIN categories ON categories.id = suggestions.category_canonical_id
            WHERE transactions.source_transaction_id = 'llm-review'
              AND suggestions.status = 'pending'
              AND suggestions.suggested_pattern = 'Coffee Lab'
              AND suggestions.suggested_match_type = 'merchant_eq'
              AND categories.name = 'Restaurantes';
            """));
    }

    [Fact]
    public async Task Import_DegradesGracefully_WhenLlmServiceReturnsNoResult()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory(
            new StubLlmCategorizationService((_, _) => new Dictionary<int, LlmCategorizationResult>()));
        using var client = factory.CreateClient();
        await CleanImportData(databaseUrl);

        var response = await UploadWiseCsv(
            client,
            transferNumber: "llm-null",
            beneficiaryName: "Coffee Lab Lisbon");

        Assert.Equal(1, response.Imported);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE source = 'wise'
              AND source_transaction_id = 'llm-null'
              AND category_canonical_id IS NULL;
            """));
    }

    [Fact]
    public void ActivoBankDedupHash_ChangesWithRunningBalanceAndRawDescription()
    {
        var transaction = new Ingestion.ParsedTransaction(
            "COMPRA A.M.O.BREWERY",
            "A.M.O.Brewery",
            5.19m,
            "OUT",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 1),
            "EUR",
            100m,
            null,
            "completed",
            null);

        var original = Program.ComputeDedupHash(1, transaction);
        var changedBalance = Program.ComputeDedupHash(1, transaction with { RunningBalance = 94.81m });
        var changedDescription = Program.ComputeDedupHash(1, transaction with
        {
            RawDescription = "COMPRA A.M.O.BREWERY "
        });

        Assert.NotEqual(original, changedBalance);
        Assert.NotEqual(original, changedDescription);
    }

    private static async Task<ImportResponse> Upload(HttpClient client, string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        await using var stream = File.OpenRead(path);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fixture);

        var response = await client.PostAsync("/api/imports", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResponse>())!;
    }

    private static async Task<ImportResponse> UploadWiseCsv(
        HttpClient client,
        string transferNumber,
        string beneficiaryName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(BuildWiseCsv(transferNumber, beneficiaryName)), "file", "llm.csv");

        var response = await client.PostAsync("/api/imports", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResponse>())!;
    }

    private static async Task CleanImportData(string databaseUrl)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE rule_suggestions, transactions, accounts, import_batches RESTART IDENTITY CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildWiseCsv(string transferNumber, string beneficiaryName) =>
        string.Join(
            Environment.NewLine,
            "Número da transferência,Situação,Direção,Criada em,Concluída em,Nome de origem,Valor de origem (tarifas inclusas),Moeda de origem,Nome do beneficiário,Referência,Criada por,Categoria,Mensagem",
            $"{transferNumber},COMPLETED,OUT,2026-06-09 09:00:00,2026-06-09 09:00:00,Conta Wise,12.34,EUR,{beneficiaryName},Cafe,Main Account,,,");

    private static async Task<int> ScalarInt(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record ImportResponse(int Imported, int Ignored);

    private sealed class ApiFactory(
        ILlmCategorizationService? llmCategorizationService = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (configurationOverrides is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(configurationOverrides);
                });
            }

            if (llmCategorizationService is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ILlmCategorizationService>();
                    services.AddSingleton(llmCategorizationService);
                });
            }
        }
    }

    private sealed class DatabaseUrlScope : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("DATABASE_URL");

        public DatabaseUrlScope(string databaseUrl) =>
            Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);

        public void Dispose() =>
            Environment.SetEnvironmentVariable("DATABASE_URL", _previousValue);
    }

    private sealed class StubLlmCategorizationService(
        Func<IReadOnlyList<LlmCategorizationInput>, IReadOnlyList<string>, IReadOnlyDictionary<int, LlmCategorizationResult>> handler)
        : ILlmCategorizationService
    {
        public Task<IReadOnlyDictionary<int, LlmCategorizationResult>> CategorizeAsync(
            IReadOnlyList<LlmCategorizationInput> transactions,
            IReadOnlyList<string> canonicalCategories,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(transactions, canonicalCategories));
    }
}
