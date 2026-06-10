using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class AnomalyEndpointTests
{
    [Fact]
    public async Task GetAnomalies_ReturnsExpectedAprilAndMayAnomalies()
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
        await CleanImportData(databaseUrl);

        await Upload(client, "activo_2026_004.pdf");
        await Upload(client, "activo_2026_005.pdf");

        var april = await client.GetFromJsonAsync<AnomaliesResponse>("/api/anomalies?month=2026-04");
        var may = await client.GetFromJsonAsync<AnomaliesResponse>("/api/anomalies?month=2026-05");

        Assert.NotNull(april);
        Assert.NotNull(may);
        Assert.Equal(AnomalyDetector.DetectionMethod, april!.DetectionMethod);
        Assert.Equal(AnomalyDetector.DetectionMethod, may!.DetectionMethod);

        Assert.Contains(april.Anomalies, anomaly =>
            anomaly.NormalizedMerchant.Contains("Motosolucao", StringComparison.OrdinalIgnoreCase) &&
            anomaly.Amount == 931.36m);
        Assert.Contains(april.Anomalies, anomaly =>
            anomaly.NormalizedMerchant.Contains("Motosolucao", StringComparison.OrdinalIgnoreCase) &&
            anomaly.Amount == 357.92m);
        Assert.Contains(may.Anomalies, anomaly =>
            anomaly.NormalizedMerchant.Contains("Alessandra", StringComparison.OrdinalIgnoreCase) &&
            anomaly.Amount == 1600m);
        Assert.Contains(may.Anomalies, anomaly =>
            anomaly.NormalizedMerchant.Contains("Reembolso Irs", StringComparison.OrdinalIgnoreCase) &&
            anomaly.Amount == 3784.62m);

        Assert.DoesNotContain(april.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Una Seguros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Una Seguros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Isabel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Isabel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Vencimento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Vencimento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Metropolitano", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may.Anomalies, anomaly => anomaly.NormalizedMerchant.Contains("Brewery", StringComparison.OrdinalIgnoreCase));
        Assert.All(april.Anomalies.Concat(may.Anomalies), anomaly => Assert.Null(anomaly.Explanation));
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

    private sealed record AnomaliesResponse(
        string Month,
        string DetectionMethod,
        IReadOnlyList<AnomalyItem> Anomalies);

    private sealed record AnomalyItem(
        int TransactionId,
        string NormalizedMerchant,
        decimal Amount,
        string Direction,
        string? Category,
        decimal DeviationFactor,
        string? Explanation);

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
