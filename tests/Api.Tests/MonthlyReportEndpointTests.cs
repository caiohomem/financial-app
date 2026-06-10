using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class MonthlyReportEndpointTests
{
    [Fact]
    public async Task GetMonthlyReport_ReturnsDeterministicAggregationsAndStubbedNarrative()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var databaseScope = new EnvironmentVariableScope("DATABASE_URL", databaseUrl);
        using var anthropicScope = new EnvironmentVariableScope("ANTHROPIC_API_KEY", null);
        await CleanImportData(databaseUrl);

        await using var factory = new ApiFactory(new StubMonthlyReportService());
        using var client = factory.CreateClient();

        await Upload(client, "activo_2026_004.pdf");
        await Upload(client, "activo_2026_005.pdf");

        var response = await client.GetFromJsonAsync<MonthlyReportResponse>("/api/reports/monthly?month=2026-05");
        Assert.NotNull(response);

        var expected = await LoadExpectedTotals(databaseUrl, "2026-05");

        Assert.Equal("2026-05", response!.Month);
        Assert.Equal(expected.TotalOut, response.Aggregations.TotalOut);
        Assert.Equal(expected.TotalIn, response.Aggregations.TotalIn);
        Assert.Equal(expected.TransactionCount, response.Aggregations.TransactionCount);
        Assert.Equal(expected.PriorMonthTotalOut, response.Aggregations.PriorMonthTotalOut);
        Assert.NotEmpty(response.Aggregations.TopCategories);
        Assert.NotEmpty(response.Aggregations.TopMerchants);
        Assert.Equal(
            $"out={expected.TotalOut:F2};in={expected.TotalIn:F2};count={expected.TransactionCount};anomalies={response.Anomalies.Count}",
            response.Report);
    }

    [Fact]
    public async Task GetMonthlyReport_ForMonthWithoutTransactions_ReturnsZeroesAndNullReport()
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

        var response = await client.GetFromJsonAsync<MonthlyReportResponse>("/api/reports/monthly?month=2099-01");
        Assert.NotNull(response);

        Assert.Equal("2099-01", response!.Month);
        Assert.Equal(0m, response.Aggregations.TotalOut);
        Assert.Equal(0m, response.Aggregations.TotalIn);
        Assert.Equal(0, response.Aggregations.TransactionCount);
        Assert.Equal(0m, response.Aggregations.PriorMonthTotalOut);
        Assert.Empty(response.Aggregations.TopCategories);
        Assert.Empty(response.Aggregations.TopMerchants);
        Assert.Empty(response.Anomalies);
        Assert.Null(response.Report);
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

    private static async Task<ExpectedTotals> LoadExpectedTotals(string databaseUrl, string month)
    {
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH bounds AS (
                SELECT
                    to_date(@month || '-01', 'YYYY-MM-DD')::date AS month_start,
                    (to_date(@month || '-01', 'YYYY-MM-DD') + INTERVAL '1 month')::date AS next_month,
                    (to_date(@month || '-01', 'YYYY-MM-DD') - INTERVAL '1 month')::date AS prior_month
            )
            SELECT
                COALESCE(SUM(amount) FILTER (
                    WHERE direction = 'OUT'
                      AND booking_date >= bounds.month_start
                      AND booking_date < bounds.next_month
                ), 0) AS total_out,
                COALESCE(SUM(amount) FILTER (
                    WHERE direction = 'IN'
                      AND booking_date >= bounds.month_start
                      AND booking_date < bounds.next_month
                ), 0) AS total_in,
                COUNT(*) FILTER (
                    WHERE booking_date >= bounds.month_start
                      AND booking_date < bounds.next_month
                ) AS transaction_count,
                COALESCE(SUM(amount) FILTER (
                    WHERE direction = 'OUT'
                      AND booking_date >= bounds.prior_month
                      AND booking_date < bounds.month_start
                ), 0) AS prior_total_out
            FROM transactions
            CROSS JOIN bounds
            WHERE status <> 'cancelled'
              AND booking_date >= bounds.prior_month
              AND booking_date < bounds.next_month;
            """;
        command.Parameters.AddWithValue("month", month);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new ExpectedTotals(
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            Convert.ToInt32(reader.GetInt64(2)),
            reader.GetDecimal(3));
    }

    private sealed record ExpectedTotals(
        decimal TotalOut,
        decimal TotalIn,
        int TransactionCount,
        decimal PriorMonthTotalOut);

    private sealed record MonthlyReportResponse(
        string Month,
        MonthlyAggregationsDto Aggregations,
        IReadOnlyList<AnomalyDto> Anomalies,
        string? Report);

    private sealed record MonthlyAggregationsDto(
        string Month,
        decimal TotalOut,
        decimal TotalIn,
        int TransactionCount,
        decimal? PriorMonthTotalOut,
        IReadOnlyList<MonthlyCategorySummaryDto> TopCategories,
        IReadOnlyList<MonthlyMerchantSummaryDto> TopMerchants);

    private sealed record MonthlyCategorySummaryDto(string Name, decimal TotalOut, int Count);

    private sealed record MonthlyMerchantSummaryDto(string Name, decimal TotalOut, int Count);

    private sealed record AnomalyDto(
        int TransactionId,
        string NormalizedMerchant,
        decimal Amount,
        string Direction,
        string? Category,
        decimal DeviationFactor);

    private sealed class ApiFactory(IMonthlyReportService? reportService = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (reportService is null)
            {
                return;
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMonthlyReportService>();
                services.AddSingleton(reportService);
            });
        }
    }

    private sealed class StubMonthlyReportService : IMonthlyReportService
    {
        public Task<string?> GenerateReportAsync(
            MonthlyAggregations aggregations,
            IReadOnlyList<AnomalyDetector.AnomalyResult> anomalies,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(
                $"out={aggregations.TotalOut:F2};in={aggregations.TotalIn:F2};count={aggregations.TransactionCount};anomalies={anomalies.Count}");
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
