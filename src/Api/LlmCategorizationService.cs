using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public interface ILlmCategorizationService
{
    Task<IReadOnlyDictionary<int, LlmCategorizationResult>> CategorizeAsync(
        IReadOnlyList<LlmCategorizationInput> transactions,
        IReadOnlyList<string> canonicalCategories,
        CancellationToken cancellationToken = default);
}

public sealed class LlmCategorizationService(
    HttpClient httpClient,
    ILogger<LlmCategorizationService> logger) : ILlmCategorizationService
{
    private const string Model = "claude-haiku-4-5-20251001";
    private static readonly IReadOnlyDictionary<int, LlmCategorizationResult> EmptyResults =
        new Dictionary<int, LlmCategorizationResult>();
    private readonly string? _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public async Task<IReadOnlyDictionary<int, LlmCategorizationResult>> CategorizeAsync(
        IReadOnlyList<LlmCategorizationInput> transactions,
        IReadOnlyList<string> canonicalCategories,
        CancellationToken cancellationToken = default)
    {
        if (transactions.Count == 0 || string.IsNullOrWhiteSpace(_apiKey))
        {
            return EmptyResults;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    model = Model,
                    max_tokens = 2048,
                    system = BuildSystemPrompt(canonicalCategories),
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = BuildUserPrompt(transactions)
                        }
                    }
                })
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var envelope = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = ExtractContentText(envelope.RootElement)
                ?? throw new JsonException("Anthropic response did not contain text content.");

            return ParseResults(content, canonicalCategories);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LLM categorization request timed out. Import will continue without AI categorization.");
            return EmptyResults;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "LLM categorization failed. Import will continue without AI categorization.");
            return EmptyResults;
        }
    }

    private static string BuildSystemPrompt(IReadOnlyList<string> canonicalCategories) =>
        $$"""
        You are a financial transaction categorizer.
        For each transaction, return a JSON object with a top-level "results" array.
        Each item in "results" must contain exactly these fields:
        - "transaction_id": the transaction id provided in the input
        - "category": one of the canonical categories listed below
        - "confidence": a number between 0.0 and 1.0
        - "rule_suggestion": null OR {"pattern": "...", "match_type": "contains"|"regex"|"merchant_eq"}

        Canonical categories: {{string.Join(", ", canonicalCategories)}}

        PRIVACY NOTE: transaction descriptions are sent to this API for classification purposes only.
        Respond with valid JSON only. Do not use markdown.
        """;

    private static string BuildUserPrompt(IReadOnlyList<LlmCategorizationInput> transactions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Categorize the following transactions:");

        foreach (var transaction in transactions)
        {
            builder.AppendLine($"TransactionId: {transaction.TransactionId}");
            builder.AppendLine($"Merchant: {transaction.NormalizedMerchant ?? "(null)"}");
            builder.AppendLine($"Description: {transaction.RawDescription}");
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    private static string? ExtractContentText(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "text", StringComparison.Ordinal) &&
                item.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<int, LlmCategorizationResult> ParseResults(
        string content,
        IReadOnlyList<string> canonicalCategories)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("LLM response is missing the 'results' array.");
        }

        var allowedCategories = new HashSet<string>(canonicalCategories, StringComparer.Ordinal);
        var parsed = new Dictionary<int, LlmCategorizationResult>();

        foreach (var item in results.EnumerateArray())
        {
            var transactionId = item.GetProperty("transaction_id").GetInt32();
            var category = item.GetProperty("category").GetString()
                ?? throw new JsonException("LLM response category cannot be null.");
            var confidence = item.GetProperty("confidence").GetDouble();

            if (!allowedCategories.Contains(category))
            {
                continue;
            }

            parsed[transactionId] = new LlmCategorizationResult(
                transactionId,
                category,
                confidence,
                ParseSuggestion(item));
        }

        return parsed;
    }

    private static LlmRuleSuggestion? ParseSuggestion(JsonElement item)
    {
        if (!item.TryGetProperty("rule_suggestion", out var suggestion) ||
            suggestion.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var pattern = suggestion.GetProperty("pattern").GetString();
        var matchType = suggestion.GetProperty("match_type").GetString();

        if (string.IsNullOrWhiteSpace(pattern) ||
            matchType is not ("contains" or "regex" or "merchant_eq"))
        {
            return null;
        }

        return new LlmRuleSuggestion(pattern, matchType);
    }
}

public sealed record LlmCategorizationInput(
    int TransactionId,
    string? NormalizedMerchant,
    string RawDescription);

public sealed record LlmCategorizationResult(
    int TransactionId,
    string Category,
    double Confidence,
    LlmRuleSuggestion? Suggestion);

public sealed record LlmRuleSuggestion(
    string Pattern,
    string MatchType);
