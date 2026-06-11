using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests;

public class LlmCategorizationServiceTests
{
    private static readonly string[] CanonicalCategories = ["Restaurantes", "Transporte"];

    [Fact]
    public async Task CategorizeAsync_Anthropic_HighConfidence_ReturnsCategory()
    {
        using var scope = new LlmApiKeyScope(anthropicKey: "test-key");
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
    public async Task CategorizeAsync_Anthropic_RuleSuggestion_ReturnsResultWithSuggestion()
    {
        using var scope = new LlmApiKeyScope(anthropicKey: "test-key");
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
    public async Task CategorizeAsync_Anthropic_ApiFailure_ReturnsEmptyResult()
    {
        using var scope = new LlmApiKeyScope(anthropicKey: "test-key");
        using var client = new HttpClient(new ThrowingHandler());
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "Coffee Lab", "Coffee Lab Lisbon")],
            CanonicalCategories);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorizeAsync_Anthropic_MalformedJson_ReturnsEmptyResult()
    {
        using var scope = new LlmApiKeyScope(anthropicKey: "test-key");
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
    public async Task CategorizeAsync_OpenAi_HighConfidence_ReturnsCategory()
    {
        using var scope = new LlmApiKeyScope(openAiKey: "test-key");
        using var client = CreateHttpClient("""
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"results\":[{\"transaction_id\":1,\"category\":\"Transporte\",\"confidence\":0.95,\"rule_suggestion\":null}]}"
                  }
                }
              ]
            }
            """);
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(1, "Uber", "Uber *trip")],
            CanonicalCategories);

        var item = Assert.Single(result);
        Assert.Equal(1, item.Key);
        Assert.Equal("Transporte", item.Value.Category);
        Assert.Equal(0.95, item.Value.Confidence);
        Assert.Null(item.Value.Suggestion);
    }

    [Fact]
    public async Task CategorizeAsync_OpenAi_RuleSuggestion_ReturnsResultWithSuggestion()
    {
        using var scope = new LlmApiKeyScope(openAiKey: "test-key");
        using var client = CreateHttpClient("""
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"results\":[{\"transaction_id\":1,\"category\":\"Transporte\",\"confidence\":0.88,\"rule_suggestion\":{\"pattern\":\"Uber\",\"match_type\":\"contains\"}}]}"
                  }
                }
              ]
            }
            """);
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(1, "Uber", "Uber *trip")],
            CanonicalCategories);

        var item = Assert.Single(result).Value;
        Assert.Equal(0.88, item.Confidence);
        Assert.Equal(new LlmRuleSuggestion("Uber", "contains"), item.Suggestion);
    }

    [Fact]
    public async Task CategorizeAsync_OpenAi_ApiFailure_ReturnsEmptyResult()
    {
        using var scope = new LlmApiKeyScope(openAiKey: "test-key");
        using var client = new HttpClient(new ThrowingHandler());
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        var result = await service.CategorizeAsync(
            [new LlmCategorizationInput(1, "Uber", "Uber *trip")],
            CanonicalCategories);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorizeAsync_OpenAiPreferredOverAnthropic_WhenBothKeysSet()
    {
        using var scope = new LlmApiKeyScope(anthropicKey: "anthropic-key", openAiKey: "openai-key");
        var capturedUri = (Uri?)null;
        using var client = new HttpClient(new CapturingHandler(
            capturedRequest => capturedUri = capturedRequest.RequestUri,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"{\"results\":[]}"}}]}""",
                    Encoding.UTF8, "application/json")
            }));
        var service = new LlmCategorizationService(client, NullLogger<LlmCategorizationService>.Instance);

        await service.CategorizeAsync(
            [new LlmCategorizationInput(0, "X", "X")],
            CanonicalCategories);

        Assert.NotNull(capturedUri);
        Assert.Contains("openai.com", capturedUri!.Host);
    }

    [Fact]
    public async Task CategorizeAsync_WithoutAnyApiKey_ReturnsEmptyResultWithoutCallingApi()
    {
        using var scope = new LlmApiKeyScope();
        using var client = new HttpClient(new ThrowingHandler());
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
        }));

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

    private sealed class CapturingHandler(
        Action<HttpRequestMessage> capture,
        HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            capture(request);
            return Task.FromResult(response);
        }
    }

    private sealed class LlmApiKeyScope : IDisposable
    {
        private readonly string? _previousAnthropic = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        private readonly string? _previousOpenAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        public LlmApiKeyScope(string? anthropicKey = null, string? openAiKey = null)
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropicKey);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", openAiKey);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", _previousAnthropic);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", _previousOpenAi);
        }
    }
}
