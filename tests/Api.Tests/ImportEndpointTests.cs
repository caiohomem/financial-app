using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public void ActivoBankDedupHash_ChangesWithRunningBalanceAndRawDescription()
    {
        var transaction = new Ingestion.ParsedTransaction(
            "COMPRA A.M.O.BREWERY",
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

    private static async Task CleanImportData(string databaseUrl)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE transactions, accounts, import_batches RESTART IDENTITY CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarInt(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record ImportResponse(int Imported, int Ignored);

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { }
    }

    private sealed class DatabaseUrlScope : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("DATABASE_URL");

        public DatabaseUrlScope(string databaseUrl) =>
            Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);

        public void Dispose() =>
            Environment.SetEnvironmentVariable("DATABASE_URL", _previousValue);
    }
}
