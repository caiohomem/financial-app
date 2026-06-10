using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public class MonthlyReportServiceTests
{
    [Fact]
    public async Task GenerateReportAsync_ReturnsNarrative_WhenApiResponds()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = CreateHttpClient("""
            {
              "content": [
                {
                  "type": "text",
                  "text": "Resumo mensal gerado."
                }
              ]
            }
            """);
        var service = new MonthlyReportService(client, NullLogger<MonthlyReportService>.Instance);

        var result = await service.GenerateReportAsync(CreateAggregations(), CreateAnomalies());

        Assert.Equal("Resumo mensal gerado.", result);
    }

    [Fact]
    public async Task GenerateReportAsync_ReturnsNull_WhenApiKeyAbsent()
    {
        using var scope = new AnthropicApiKeyScope(null);
        using var client = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };
        var service = new MonthlyReportService(client, NullLogger<MonthlyReportService>.Instance);

        var result = await service.GenerateReportAsync(CreateAggregations(), CreateAnomalies());

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateReportAsync_ReturnsNull_WhenApiFails()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };
        var service = new MonthlyReportService(client, NullLogger<MonthlyReportService>.Instance);

        var result = await service.GenerateReportAsync(CreateAggregations(), CreateAnomalies());

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateReportAsync_ReturnsNull_WhenResponseMalformed()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = CreateHttpClient("""
            {
              "content": [
                {
                  "type": "text",
                  "text": ""
                }
              ]
            }
            """);
        var service = new MonthlyReportService(client, NullLogger<MonthlyReportService>.Instance);

        var result = await service.GenerateReportAsync(CreateAggregations(), CreateAnomalies());

        Assert.Null(result);
    }

    private static MonthlyAggregations CreateAggregations() =>
        new(
            "2026-05",
            1234.56m,
            2500m,
            12,
            1000m,
            [new MonthlyCategorySummary("Restaurantes", 300m, 4)],
            [new MonthlyMerchantSummary("Coffee Lab", 120m, 2)]);

    private static IReadOnlyList<AnomalyDetector.AnomalyResult> CreateAnomalies() =>
    [
        new(
            1,
            "Coffee Lab",
            "Coffee Lab Lisbon",
            95m,
            "OUT",
            "Restaurantes",
            new DateOnly(2026, 5, 10),
            2.4m,
            40m,
            80m)
    ];

    private static HttpClient CreateHttpClient(string content) =>
        new(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("boom");
    }

    private sealed class AnthropicApiKeyScope : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        public AnthropicApiKeyScope(string? value)
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", value);
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", _previousValue);
    }
}
