using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public sealed class AnomalyExplainer(HttpClient httpClient, IConfiguration configuration, ILogger<AnomalyExplainer> logger)
{
    private const string Model = "claude-haiku-4-5-20251001";
    private const string AnthropicVersion = "2023-06-01";
    private readonly HttpClient _httpClient = httpClient;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<AnomalyExplainer> _logger = logger;

    public async Task<string?> ExplainAsync(AnomalyDetector.AnomalyResult anomaly, CancellationToken cancellationToken)
    {
        var apiKey = _configuration["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new AnthropicRequest(
            Model,
            120,
            [
                new AnthropicMessage(
                    "user",
                    [
                        new AnthropicContent(
                            "text",
                            BuildPrompt(anomaly))
                    ])
            ]));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic anomaly explanation failed with status code {StatusCode}.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: timeoutCts.Token);
            return payload?.Content?
                .Select(item => item.Text?.Trim())
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Anthropic anomaly explanation timed out.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Anthropic anomaly explanation failed.");
            return null;
        }
    }

    private static string BuildPrompt(AnomalyDetector.AnomalyResult anomaly)
    {
        var category = string.IsNullOrWhiteSpace(anomaly.Category) ? "sem categoria" : anomaly.Category;
        var amount = anomaly.Amount.ToString("F2", CultureInfo.InvariantCulture);
        var factor = anomaly.DeviationFactor.ToString("F1", CultureInfo.InvariantCulture);

        return
            $"Gera uma explicação curta em português de Portugal, com no máximo 2 frases, para esta anomalia financeira. " +
            $"Mercador: '{anomaly.NormalizedMerchant}'. Categoria: '{category}'. Valor: {amount} EUR. " +
            $"{factor}x acima do habitual. Não uses listas nem markdown.";
    }

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContent> Content);

    private sealed record AnthropicContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicTextBlock>? Content);

    private sealed record AnthropicTextBlock(
        [property: JsonPropertyName("text")] string? Text);
}
