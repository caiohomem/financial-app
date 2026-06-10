using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Api.Tests;

public class ReviewEndpointsTests
{
    [Fact]
    public async Task GetReviewTransactions_ReturnsUncategorizedAndPendingSuggestionRows()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await CleanReviewData(databaseUrl);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        var restaurantsId = await GetCategoryId(connection, "Restaurantes");
        var outrosId = await GetCategoryId(connection, "Outros");
        var accountId = await InsertAccount(connection, "review-account");
        var batchId = await InsertBatch(connection);

        var uncategorizedId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Review Merchant Uncat",
            "Review Merchant Uncat raw",
            null);
        var suggestionTransactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Review Merchant Suggestion",
            "Review Merchant Suggestion raw",
            outrosId);
        _ = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Review Merchant Hidden",
            "Review Merchant Hidden raw",
            outrosId);
        await InsertRuleSuggestion(connection, suggestionTransactionId, restaurantsId, "Review Merchant Suggestion");

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<IReadOnlyList<ReviewTransactionDto>>("/api/review/transactions");

        Assert.NotNull(response);
        Assert.Equal(2, response!.Count);
        Assert.Contains(response, item => item.Id == uncategorizedId && item.CategoryCanonicalId is null);
        Assert.Contains(response, item => item.Id == suggestionTransactionId && item.CategoryCanonicalId == outrosId);
    }

    [Fact]
    public async Task PatchTransactionCategory_CreatesRuleAndRecategorizesMatchingTransactions()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await CleanReviewData(databaseUrl);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        var restaurantsId = await GetCategoryId(connection, "Restaurantes");
        var accountId = await InsertAccount(connection, "review-account");
        var batchId = await InsertBatch(connection);

        var transactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Issue15 Patch Merchant",
            "Issue15 Patch Merchant raw",
            null);
        var matchingTransactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Issue15 Patch Merchant",
            "Issue15 Patch Merchant second raw",
            null);

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/transactions/{transactionId}/category")
        {
            Content = JsonContent.Create(new
            {
                categoryId = restaurantsId,
                createRule = true,
                matchType = "merchant_eq",
                pattern = "Issue15 Patch Merchant"
            })
        };

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rules
            WHERE pattern = 'Issue15 Patch Merchant'
              AND match_type = 'merchant_eq'
              AND category_canonical_id = @categoryId
              AND priority = 200;
            """,
            ("categoryId", restaurantsId)));
        Assert.Equal(2, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE id IN (@transactionId, @matchingTransactionId)
              AND category_canonical_id = @categoryId;
            """,
            ("transactionId", transactionId),
            ("matchingTransactionId", matchingTransactionId),
            ("categoryId", restaurantsId)));
    }

    [Fact]
    public async Task ApproveRuleSuggestion_CreatesRuleAndRecategorizesMatchingTransactions()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await CleanReviewData(databaseUrl);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        var restaurantsId = await GetCategoryId(connection, "Restaurantes");
        var accountId = await InsertAccount(connection, "review-account");
        var batchId = await InsertBatch(connection);

        var transactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Issue15 Approve Merchant",
            "Issue15 Approve Merchant raw",
            null);
        var matchingTransactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Issue15 Approve Merchant",
            "Issue15 Approve Merchant second raw",
            null);
        var suggestionId = await InsertRuleSuggestion(
            connection,
            transactionId,
            restaurantsId,
            "Issue15 Approve Merchant");

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/review/rule-suggestions/{suggestionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rules
            WHERE pattern = 'Issue15 Approve Merchant'
              AND match_type = 'merchant_eq'
              AND category_canonical_id = @categoryId
              AND priority = 200;
            """,
            ("categoryId", restaurantsId)));
        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rule_suggestions
            WHERE id = @id
              AND status = 'approved';
            """,
            ("id", suggestionId)));
        Assert.Equal(2, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM transactions
            WHERE id IN (@transactionId, @matchingTransactionId)
              AND category_canonical_id = @categoryId;
            """,
            ("transactionId", transactionId),
            ("matchingTransactionId", matchingTransactionId),
            ("categoryId", restaurantsId)));
    }

    [Fact]
    public async Task RejectRuleSuggestion_UpdatesStatusOnly()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var scope = new DatabaseUrlScope(databaseUrl);
        await CleanReviewData(databaseUrl);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        var restaurantsId = await GetCategoryId(connection, "Restaurantes");
        var accountId = await InsertAccount(connection, "review-account");
        var batchId = await InsertBatch(connection);

        var transactionId = await InsertTransaction(
            connection,
            accountId,
            batchId,
            "Issue15 Reject Merchant",
            "Issue15 Reject Merchant raw",
            null);
        var suggestionId = await InsertRuleSuggestion(
            connection,
            transactionId,
            restaurantsId,
            "Issue15 Reject Merchant");

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/review/rule-suggestions/{suggestionId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rule_suggestions
            WHERE id = @id
              AND status = 'rejected';
            """,
            ("id", suggestionId)));
        Assert.Equal(0, await ScalarInt(
            connection,
            """
            SELECT count(*)
            FROM rules
            WHERE pattern = 'Issue15 Reject Merchant';
            """));
    }

    private static async Task CleanReviewData(string databaseUrl)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE rule_suggestions, transactions, accounts, import_batches RESTART IDENTITY CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetCategoryId(NpgsqlConnection connection, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM categories WHERE name = @name;";
        command.Parameters.AddWithValue("name", name);
        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private static async Task<int> InsertAccount(NpgsqlConnection connection, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (name, source, currency)
            VALUES (@name, 'wise', 'EUR')
            RETURNING id;
            """;
        command.Parameters.AddWithValue("name", name);
        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private static async Task<int> InsertBatch(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO import_batches (source, filename, row_count)
            VALUES ('wise', 'review.csv', 1)
            RETURNING id;
            """;
        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private static async Task<int> InsertTransaction(
        NpgsqlConnection connection,
        int accountId,
        int batchId,
        string normalizedMerchant,
        string rawDescription,
        int? categoryId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transactions (
                account_id, source, source_transaction_id, booking_date, raw_description,
                normalized_merchant, amount, direction, currency, status, category_canonical_id, import_batch_id
            )
            VALUES (
                @accountId, 'wise', @sourceTransactionId, DATE '2026-06-10', @rawDescription,
                @normalizedMerchant, 12.34, 'OUT', 'EUR', 'completed', @categoryId, @batchId
            )
            RETURNING id;
            """;
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("sourceTransactionId", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("rawDescription", rawDescription);
        command.Parameters.AddWithValue("normalizedMerchant", normalizedMerchant);
        if (categoryId is null)
        {
            command.Parameters.AddWithValue("categoryId", NpgsqlDbType.Integer, DBNull.Value);
        }
        else
        {
            command.Parameters.AddWithValue("categoryId", categoryId.Value);
        }

        command.Parameters.AddWithValue("batchId", batchId);
        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private static async Task<int> InsertRuleSuggestion(
        NpgsqlConnection connection,
        int transactionId,
        int categoryId,
        string pattern)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO rule_suggestions (
                transaction_id, suggested_pattern, suggested_match_type, category_canonical_id, confidence, status
            )
            VALUES (
                @transactionId, @pattern, 'merchant_eq', @categoryId, 0.82, 'pending'
            )
            RETURNING id;
            """;
        command.Parameters.AddWithValue("transactionId", transactionId);
        command.Parameters.AddWithValue("pattern", pattern);
        command.Parameters.AddWithValue("categoryId", categoryId);
        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private static async Task<int> ScalarInt(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32((await command.ExecuteScalarAsync())!);
    }

    private sealed record ReviewTransactionDto(int Id, int? CategoryCanonicalId);

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
}
