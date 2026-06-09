using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class MigrationTests
{
    [Fact]
    public async Task Startup_AppliesInitialSchema_OnCleanDatabase()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseUrlScope = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        await AssertRelationExists(connection, "transactions");
        await AssertRelationExists(connection, "import_batches");
        await AssertRelationExists(connection, "idx_transactions_source_source_transaction_id");
        await AssertRelationExists(connection, "idx_transactions_dedup_hash");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO import_batches (source, filename, row_count)
            VALUES ('wise', 'batch.csv', 1)
            RETURNING id;
            """;
        var importBatchId = (int)(await command.ExecuteScalarAsync())!;

        await using var insertAccount = connection.CreateCommand();
        insertAccount.CommandText = """
            INSERT INTO accounts (name, source, currency)
            VALUES ('Main', 'wise', 'EUR')
            RETURNING id;
            """;
        var accountId = (int)(await insertAccount.ExecuteScalarAsync())!;

        await using var invalidTransaction = connection.CreateCommand();
        invalidTransaction.CommandText = """
            INSERT INTO transactions (
                account_id, source, booking_date, raw_description, amount,
                direction, currency, status, import_batch_id
            )
            VALUES (
                @accountId, 'wise', DATE '2026-06-09', 'raw', -1,
                'OUT', 'EUR', 'completed', @importBatchId
            );
            """;
        invalidTransaction.Parameters.AddWithValue("accountId", accountId);
        invalidTransaction.Parameters.AddWithValue("importBatchId", importBatchId);

        await Assert.ThrowsAsync<PostgresException>(() => invalidTransaction.ExecuteNonQueryAsync());
    }

    private static async Task AssertRelationExists(NpgsqlConnection connection, string relationName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@relationName)";
        command.Parameters.AddWithValue("relationName", $"public.{relationName}");

        var result = await command.ExecuteScalarAsync();
        Assert.Equal($"public.{relationName}", result);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { }
    }

    private sealed class DatabaseUrlScope : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("DATABASE_URL");

        public DatabaseUrlScope(string databaseUrl)
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", _previousValue);
        }
    }
}
