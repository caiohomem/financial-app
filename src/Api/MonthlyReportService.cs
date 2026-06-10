using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public interface IMonthlyReportService
{
    Task<string?> GenerateReportAsync(
        MonthlyAggregations aggregations,
        IReadOnlyList<AnomalyDetector.AnomalyResult> anomalies,
        CancellationToken cancellationToken = default);
}

public sealed class MonthlyReportService(
    HttpClient httpClient,
    ILogger<MonthlyReportService> logger) : IMonthlyReportService
{
    private const string Model = "claude-haiku-4-5-20251001";
    private readonly string? _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public async Task<string?> GenerateReportAsync(
        MonthlyAggregations aggregations,
        IReadOnlyList<AnomalyDetector.AnomalyResult> anomalies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        ArgumentNullException.ThrowIfNull(anomalies);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    model = Model,
                    max_tokens = 600,
                    system = BuildSystemPrompt(),
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = BuildUserPrompt(aggregations, anomalies)
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
            return ExtractContentText(envelope.RootElement)
                ?? throw new JsonException("Anthropic response did not contain text content.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Monthly report request timed out. Response will omit the narrative.");
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Monthly report generation failed. Response will omit the narrative.");
            return null;
        }
    }

    private static string BuildSystemPrompt() =>
        """
        És um assistente financeiro pessoal.
        Escreve um relatório mensal em português simples, sem jargão e sem markdown.
        Usa exclusivamente os números fornecidos e não inventes valores.
        Inclui um resumo geral, comparação com o mês anterior, top categorias, top merchants e uma secção curta sobre anomalias.
        """;

    private static string BuildUserPrompt(
        MonthlyAggregations aggregations,
        IReadOnlyList<AnomalyDetector.AnomalyResult> anomalies)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mês: {aggregations.Month}");
        builder.AppendLine($"Total gasto: {aggregations.TotalOut.ToString("F2", CultureInfo.InvariantCulture)} EUR");
        builder.AppendLine($"Total recebido: {aggregations.TotalIn.ToString("F2", CultureInfo.InvariantCulture)} EUR");
        builder.AppendLine($"Número de transações: {aggregations.TransactionCount}");
        builder.AppendLine(
            $"Total gasto no mês anterior: {(aggregations.PriorMonthTotalOut.HasValue ? aggregations.PriorMonthTotalOut.Value.ToString("F2", CultureInfo.InvariantCulture) : "sem dados")} EUR");
        builder.AppendLine();
        builder.AppendLine("Top categorias por gasto:");

        if (aggregations.TopCategories.Count == 0)
        {
            builder.AppendLine("- sem movimentos");
        }
        else
        {
            foreach (var category in aggregations.TopCategories)
            {
                builder.AppendLine(
                    $"- {category.Name}: {category.TotalOut.ToString("F2", CultureInfo.InvariantCulture)} EUR em {category.Count} transações");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Top merchants por gasto:");

        if (aggregations.TopMerchants.Count == 0)
        {
            builder.AppendLine("- sem movimentos");
        }
        else
        {
            foreach (var merchant in aggregations.TopMerchants)
            {
                builder.AppendLine(
                    $"- {merchant.Name}: {merchant.TotalOut.ToString("F2", CultureInfo.InvariantCulture)} EUR em {merchant.Count} transações");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Anomalias:");

        if (anomalies.Count == 0)
        {
            builder.AppendLine("- nenhuma anomalia detetada");
        }
        else
        {
            foreach (var anomaly in anomalies)
            {
                builder.AppendLine(
                    $"- {anomaly.NormalizedMerchant}: {anomaly.Amount.ToString("F2", CultureInfo.InvariantCulture)} EUR, categoria {anomaly.Category ?? "sem categoria"}, fator {anomaly.DeviationFactor.ToString("F2", CultureInfo.InvariantCulture)}");
            }
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
                var value = text.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}

public sealed record MonthlyAggregations(
    string Month,
    decimal TotalOut,
    decimal TotalIn,
    int TransactionCount,
    decimal? PriorMonthTotalOut,
    IReadOnlyList<MonthlyCategorySummary> TopCategories,
    IReadOnlyList<MonthlyMerchantSummary> TopMerchants);

public sealed record MonthlyCategorySummary(
    string Name,
    decimal TotalOut,
    int Count);

public sealed record MonthlyMerchantSummary(
    string Name,
    decimal TotalOut,
    int Count);
