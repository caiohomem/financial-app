using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public class LlmCategorizationServiceTests
{
    private static readonly string[] CanonicalCategories = ["Restaurantes", "Transporte"];

    [Fact]
    public async Task CategorizeAsync_HighConfidence_ReturnsCategory()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = CreateHttpClient("""
            {
              "content": [
                {
                  "type": "text",
                  "text": "{\"results\":[{\"transaction_id\":0,\"category\":\"Restaurantes\",\"confidence\":0.90,\"rule_suggestion\":null}]}"
                }
              ]
            }
            """);
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        var item = Assert.Single(result);
        Assert.Equal(0, item.Key);
        Assert.Equal("Restaurantes", item.Value.Category);
        Assert.Equal(0.90, item.Value.Confidence);
        Assert.Null(item.Value.Suggestion);
    }

    [Fact]
    public async Task CategorizeAsync_LowConfidence_ReturnsResultWithLowScore()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = CreateHttpClient("""
            {
              "content": [
                {
                  "type": "text",
                  "text": "{\"results\":[{\"transaction_id\":0,\"category\":\"Restaurantes\",\"confidence\":0.80,\"rule_suggestion\":{\"pattern\":\"Coffee Lab\",\"match_type\":\"merchant_eq\"}}]}"
                }
              ]
            }
            """);
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        var item = Assert.Single(result).Value;
        Assert.Equal(0.80, item.Confidence);
        Assert.Equal(new LlmRuleSuggestion("Coffee Lab", "merchant_eq"), item.Suggestion);
    }

    [Fact]
    public async Task CategorizeAsync_ApiFailure_ReturnsEmptyResult()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorizeAsync_MalformedJson_ReturnsEmptyResult()
    {
        using var scope = new AnthropicApiKeyScope("test-key");
        using var client = CreateHttpClient("""
            {
              "content": [
                {
                  "type": "text",
                  "text": "not json"
                }
              ]
            }
            """);
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorizeAsync_WithoutApiKey_ReturnsEmptyResultWithoutCallingApi()
    {
        using var scope = new AnthropicApiKeyScope(null);
        using var client = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        Assert.Empty(result);
    }

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
