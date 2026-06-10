using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class DashboardEndpointTests
{
    [Fact]
    public async Task GetAccounts_ReturnsPerAccountBalanceExcludingCancelledTransactions()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseScope = new EnvironmentVariableScope("DATABASE_URL", databaseUrl);
        using var anthropicScope = new EnvironmentVariableScope("ANTHROPIC_API_KEY", null);
        await CleanImportData(databaseUrl);

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await Upload(client, "wise_sample.csv");
        await Upload(client, "activo_2026_005.pdf");
        await InsertCancelledOutTransaction(databaseUrl, "cancelled balance test", 999.99m, "Outros", "2026-05-20");

        var response = await client.GetFromJsonAsync<IReadOnlyList<AccountBalanceDto>>("/api/accounts");
        Assert.NotNull(response);

        var expected = await LoadExpectedAccountBalances(databaseUrl);

        Assert.Equal(expected.Count, response!.Count);
        foreach (var item in response)
        {
            var expectedBalance = Assert.Single(expected, expectedItem => expectedItem.Id == item.Id);
            Assert.Equal(expectedBalance.Name, item.Name);
            Assert.Equal(expectedBalance.Source, item.Source);
            Assert.Equal(expectedBalance.Currency, item.Currency);
            Assert.Equal(expectedBalance.Balance, item.Balance);
        }
    }

    [Fact]
    public async Task GetSpendingByCategory_ExcludesCancelledTransactions()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseScope = new EnvironmentVariableScope("DATABASE_URL", databaseUrl);
        using var anthropicScope = new EnvironmentVariableScope("ANTHROPIC_API_KEY", null);
        await CleanImportData(databaseUrl);

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await Upload(client, "activo_2026_005.pdf");
        await InsertCancelledOutTransaction(databaseUrl, "cancelled spending test", 999.99m, "Restaurantes", "2026-05-18");

        var response = await client.GetFromJsonAsync<SpendingResponseDto>("/api/spending-by-category?month=2026-05");
        Assert.NotNull(response);

        var expected = await LoadExpectedSpendingByCategory(databaseUrl, "2026-05");
        var actualTotal = response!.Categories.Sum(item => item.Total);

        Assert.Equal("2026-05", response.Month);
        Assert.Equal(expected.Sum(item => item.Total), actualTotal);
        Assert.Equal(expected.Count, response.Categories.Count);
        foreach (var item in response.Categories)
        {
            var expectedCategory = Assert.Single(expected, expectedItem => expectedItem.Category == item.Category);
            Assert.Equal(expectedCategory.Total, item.Total);
        }
    }

    [Fact]
    public async Task GetTransactions_AppliesMonthAccountCategoryAndSearchFilters()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseScope = new EnvironmentVariableScope("DATABASE_URL", databaseUrl);
        using var anthropicScope = new EnvironmentVariableScope("ANTHROPIC_API_KEY", null);
        await CleanImportData(databaseUrl);

        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await Upload(client, "wise_sample.csv");
        await Upload(client, "activo_2026_005.pdf");

        var target = await LoadTransactionFilterTarget(databaseUrl);
        Assert.NotNull(target);

        var url =
            $"/api/transactions?month={target!.Month}&account={target.AccountId}&category={Uri.EscapeDataString(target.Category)}&search={Uri.EscapeDataString(target.SearchTerm)}";
        var response = await client.GetFromJsonAsync<IReadOnlyList<TransactionDto>>(url);

        Assert.NotNull(response);
        Assert.NotEmpty(response!);
        Assert.All(response!, item =>
        {
            Assert.StartsWith(target.Month, item.BookingDate, StringComparison.Ordinal);
            Assert.Equal(target.Category, item.Category);
            Assert.Equal(target.AccountName, item.AccountName);
            Assert.True(
                item.NormalizedMerchant.Contains(target.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                item.RawDescription.Contains(target.SearchTerm, StringComparison.OrdinalIgnoreCase));
        });

        var expectedIds = await LoadExpectedTransactionIds(
            databaseUrl,
            target.Month,
            target.AccountId,
            target.Category,
            target.SearchTerm);

        Assert.Equal(expectedIds, response.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task GetTransactions_RejectsInvalidFilters()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseScope = new EnvironmentVariableScope("DATABASE_URL", databaseUrl);
        using var anthropicScope = new EnvironmentVariableScope("ANTHROPIC_API_KEY", null);
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var invalidMonth = await client.GetAsync("/api/transactions?month=2026-13");
        var invalidAccount = await client.GetAsync("/api/transactions?account=abc");

        Assert.Equal(HttpStatusCode.BadRequest, invalidMonth.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAccount.StatusCode);
    }

    private static async Task Upload(HttpClient client, string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        await using var stream = File.OpenRead(path);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fixture);

        var response = await client.PostAsync("/api/imports", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task CleanImportData(string databaseUrl)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE rule_suggestions, transactions, accounts, import_batches RESTART IDENTITY CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCancelledOutTransaction(
        string databaseUrl,
        string rawDescription,
        decimal amount,
        string category,
        string bookingDate)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        var accountId = await ScalarInt(connection, "SELECT id FROM accounts ORDER BY id ASC LIMIT 1;");
        var batchId = await ScalarInt(connection, "SELECT id FROM import_batches ORDER BY id ASC LIMIT 1;");
        var categoryId = await ScalarInt(
            connection,
            "SELECT id FROM categories WHERE name = @category;",
            ("category", category));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transactions (
                account_id,
                source,
                source_transaction_id,
                booking_date,
                raw_description,
                normalized_merchant,
                amount,
                direction,
                currency,
                status,
                category_canonical_id,
                import_batch_id
            )
            VALUES (
                @accountId,
                'wise',
                @sourceTransactionId,
                @bookingDate,
                @rawDescription,
                @normalizedMerchant,
                @amount,
                'OUT',
                'EUR',
                'cancelled',
                @categoryId,
                @batchId
            );
            """;
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("sourceTransactionId", $"manual-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("bookingDate", DateOnly.Parse(bookingDate));
        command.Parameters.AddWithValue("rawDescription", rawDescription);
        command.Parameters.AddWithValue("normalizedMerchant", rawDescription);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("categoryId", categoryId);
        command.Parameters.AddWithValue("batchId", batchId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<AccountBalanceDto>> LoadExpectedAccountBalances(string databaseUrl)
    {
        var rows = new List<AccountBalanceDto>();

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
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

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AccountBalanceDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<CategorySpendDto>> LoadExpectedSpendingByCategory(string databaseUrl, string month)
    {
        var rows = new List<CategorySpendDto>();

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(categories.name, 'Uncategorized') AS category,
                COALESCE(SUM(transactions.amount), 0) AS total
            FROM transactions
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE transactions.direction = 'OUT'
              AND transactions.status <> 'cancelled'
              AND TO_CHAR(transactions.booking_date, 'YYYY-MM') = @month
            GROUP BY COALESCE(categories.name, 'Uncategorized')
            ORDER BY total DESC, category ASC;
            """;
        command.Parameters.AddWithValue("month", month);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new CategorySpendDto(reader.GetString(0), reader.GetDecimal(1)));
        }

        return rows;
    }

    private static async Task<TransactionFilterTarget?> LoadTransactionFilterTarget(string databaseUrl)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                TO_CHAR(transactions.booking_date, 'YYYY-MM') AS month,
                transactions.account_id,
                accounts.name,
                COALESCE(categories.name, 'Uncategorized') AS category,
                COALESCE(NULLIF(transactions.normalized_merchant, ''), transactions.raw_description) AS merchant
            FROM transactions
            JOIN accounts ON accounts.id = transactions.account_id
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE COALESCE(NULLIF(transactions.normalized_merchant, ''), transactions.raw_description) <> ''
              AND COALESCE(categories.name, 'Uncategorized') <> 'Uncategorized'
            ORDER BY transactions.booking_date DESC, transactions.id DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var merchant = reader.GetString(4);
        var searchTerm = merchant.Length >= 4 ? merchant[..4] : merchant;

        return new TransactionFilterTarget(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            searchTerm);
    }

    private static async Task<int[]> LoadExpectedTransactionIds(
        string databaseUrl,
        string month,
        int accountId,
        string category,
        string search)
    {
        var ids = new List<int>();

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT transactions.id
            FROM transactions
            LEFT JOIN categories ON categories.id = transactions.category_canonical_id
            WHERE TO_CHAR(transactions.booking_date, 'YYYY-MM') = @month
              AND transactions.account_id = @accountId
              AND COALESCE(categories.name, 'Uncategorized') = @category
              AND (
                  COALESCE(transactions.normalized_merchant, '') ILIKE @search
                  OR transactions.raw_description ILIKE @search
              )
            ORDER BY transactions.booking_date DESC, transactions.id DESC;
            """;
        command.Parameters.AddWithValue("month", month);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("search", $"%{search}%");

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids.ToArray();
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

        return Convert.ToInt32((await command.ExecuteScalarAsync())!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record AccountBalanceDto(int Id, string Name, string Source, string Currency, decimal Balance);

    private sealed record CategorySpendDto(string Category, decimal Total);

    private sealed record SpendingResponseDto(string Month, IReadOnlyList<CategorySpendDto> Categories);

    private sealed record TransactionDto(
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

    private sealed record TransactionFilterTarget(
        string Month,
        int AccountId,
        string AccountName,
        string Category,
        string SearchTerm);

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
