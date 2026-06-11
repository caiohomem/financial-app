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
    private static readonly IReadOnlyDictionary<int, LlmCategorizationResult> EmptyResults =
        new Dictionary<int, LlmCategorizationResult>();

    private readonly string? _anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    private readonly string? _openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private readonly string? _modelOverride = Environment.GetEnvironmentVariable("LLM_MODEL");

    private const int BatchSize = 40;
    private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(90);

    private bool IsConfigured => !string.IsNullOrWhiteSpace(_anthropicKey) || !string.IsNullOrWhiteSpace(_openAiKey);

    public async Task<IReadOnlyDictionary<int, LlmCategorizationResult>> CategorizeAsync(
        IReadOnlyList<LlmCategorizationInput> transactions,
        IReadOnlyList<string> canonicalCategories,
        CancellationToken cancellationToken = default)
    {
        if (transactions.Count == 0 || !IsConfigured)
        {
            return EmptyResults;
        }

        var systemPrompt = BuildSystemPrompt(canonicalCategories);
        var batches = transactions
            .Select((t, i) => (t, i))
            .GroupBy(x => x.i / BatchSize)
            .Select(g => g.Select(x => x.t).ToList())
            .ToList();

        logger.LogInformation(
            "LLM categorization: {Total} transactions split into {Batches} batches.",
            transactions.Count, batches.Count);

        var batchTasks = batches.Select((batch, index) => ProcessBatchAsync(
            batch, index, batches.Count, systemPrompt, canonicalCategories));

        var results = await Task.WhenAll(batchTasks);

        var merged = new Dictionary<int, LlmCategorizationResult>();
        foreach (var dict in results)
        {
            foreach (var (id, result) in dict)
            {
                merged[id] = result;
            }
        }

        return merged;
    }

    private async Task<IReadOnlyDictionary<int, LlmCategorizationResult>> ProcessBatchAsync(
        IReadOnlyList<LlmCategorizationInput> batch,
        int index,
        int total,
        string systemPrompt,
        IReadOnlyList<string> canonicalCategories)
    {
        logger.LogInformation(
            "LLM categorization: starting batch {Batch}/{Total} ({Count} transactions).",
            index + 1, total, batch.Count);

        using var cts = new CancellationTokenSource(LlmTimeout);
        try
        {
            var userPrompt = BuildUserPrompt(batch);
            var content = !string.IsNullOrWhiteSpace(_openAiKey)
                ? await CallOpenAiAsync(systemPrompt, userPrompt, cts.Token)
                : await CallAnthropicAsync(systemPrompt, userPrompt, cts.Token);

            logger.LogInformation(
                "LLM categorization: batch {Batch}/{Total} raw response ({Length} chars): {Content}",
                index + 1, total, content.Length, content);

            var results = ParseResults(content, canonicalCategories);

            var missingIds = batch
                .Select(t => t.TransactionId)
                .Where(id => !results.ContainsKey(id))
                .ToList();
            logger.LogInformation(
                "LLM categorization: batch {Batch}/{Total} done — {Count}/{BatchSize} results parsed. Missing ids: [{Missing}]",
                index + 1, total, results.Count, batch.Count, string.Join(",", missingIds));
            return results;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning(
                "LLM categorization: batch {Batch}/{Total} TIMED OUT after {Timeout}s — returning empty results.",
                index + 1, total, LlmTimeout.TotalSeconds);
            return EmptyResults;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "LLM categorization: batch {Batch}/{Total} failed: {Message}", index + 1, total, exception.Message);
            return EmptyResults;
        }
    }

    private async Task<string> CallAnthropicAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var model = _modelOverride ?? "claude-haiku-4-5-20251001";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model,
                max_tokens = 4096,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = userPrompt } }
            })
        };
        request.Headers.Add("x-api-key", _anthropicKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var envelope = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!envelope.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Anthropic response did not contain a content array.");
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "text", StringComparison.Ordinal) &&
                item.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? throw new JsonException("Anthropic text content was null.");
            }
        }

        throw new JsonException("Anthropic response did not contain text content.");
    }

    private async Task<string> CallOpenAiAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var model = _modelOverride ?? "gpt-4o-mini";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                max_tokens = 4096,
                response_format = new { type = "json_object" },
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            })
        };
        request.Headers.Add("Authorization", $"Bearer {_openAiKey}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var envelope = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!envelope.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new JsonException("OpenAI response did not contain choices.");
        }

        return choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?? throw new JsonException("OpenAI message content was null.");
    }

    private static string BuildSystemPrompt(IReadOnlyList<string> canonicalCategories) =>
        $$"""
        You are a financial transaction categorizer.
        For each transaction, return a JSON object with a top-level "results" array.
        Each item in "results" must contain exactly these fields:
        - "transaction_id": the transaction id provided in the input
        - "category": one of the canonical categories listed below, OR null if none fits well
        - "confidence": a number between 0.0 and 1.0
        - "new_category": null OR a short Portuguese category name (e.g. "Saúde", "Viagens") when category is null and you are confident a new category is needed
        - "rule_suggestion": null OR {"pattern": "...", "match_type": "contains"|"regex"|"merchant_eq"}

        Use "new_category" only when NO existing category fits and confidence would be below 0.55.
        When "new_category" is set, set "category" to null.

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
            var confidence = item.GetProperty("confidence").GetDouble();

            string? category = null;
            if (item.TryGetProperty("category", out var catProp) &&
                catProp.ValueKind == JsonValueKind.String)
            {
                category = catProp.GetString();
            }

            string? newCategoryName = null;
            if (item.TryGetProperty("new_category", out var newCatProp) &&
                newCatProp.ValueKind == JsonValueKind.String)
            {
                newCategoryName = newCatProp.GetString()?.Trim();
                if (string.IsNullOrEmpty(newCategoryName)) newCategoryName = null;
            }

            // Valid result: either a known category, or a new_category proposal
            if (category is not null && !allowedCategories.Contains(category))
            {
                category = null;
            }

            if (category is null && newCategoryName is null)
            {
                continue;
            }

            parsed[transactionId] = new LlmCategorizationResult(
                transactionId,
                category,
                confidence,
                ParseSuggestion(item),
                newCategoryName);
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
    string? Category,
    double Confidence,
    LlmRuleSuggestion? Suggestion,
    string? NewCategoryName = null);

public sealed record LlmRuleSuggestion(
    string Pattern,
    string MatchType);
