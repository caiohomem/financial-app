using System.Text.RegularExpressions;
using Npgsql;

public sealed class CategoryEvaluator
{
    private readonly IReadOnlyList<RuleRow> _rules;
    private readonly IReadOnlyDictionary<string, int> _sourceMappings;

    public CategoryEvaluator(
        IReadOnlyList<RuleRow> rules,
        IReadOnlyDictionary<string, int> sourceMappings)
    {
        _rules = rules;
        _sourceMappings = sourceMappings;
    }

    public int? Evaluate(string? categorySource, string? normalizedMerchant, string rawDescription)
    {
        if (!string.IsNullOrWhiteSpace(categorySource) &&
            _sourceMappings.TryGetValue(categorySource, out var mappedCategoryId))
        {
            return mappedCategoryId;
        }

        foreach (var rule in _rules)
        {
            if (Matches(rule, normalizedMerchant, rawDescription))
            {
                return rule.CategoryCanonicalId;
            }
        }

        return null;
    }

    public sealed record RuleRow(
        int Id,
        string Pattern,
        string MatchType,
        int CategoryCanonicalId,
        int Priority);

    public static async Task<CategoryEvaluator> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        var rules = new List<RuleRow>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, pattern, match_type, category_canonical_id, priority
                FROM rules
                ORDER BY priority DESC, id ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rules.Add(new RuleRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4)));
            }
        }

        var sourceMappings = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT source_label, category_canonical_id
                FROM category_mappings
                WHERE source = 'wise';
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sourceMappings[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        return new CategoryEvaluator(rules, sourceMappings);
    }

    private static bool Matches(RuleRow rule, string? normalizedMerchant, string rawDescription) =>
        rule.MatchType switch
        {
            "merchant_eq" => normalizedMerchant is not null &&
                             string.Equals(
                                 normalizedMerchant,
                                 rule.Pattern,
                                 StringComparison.OrdinalIgnoreCase),
            "contains" => Contains(normalizedMerchant, rule.Pattern) ||
                          Contains(rawDescription, rule.Pattern),
            "regex" => Regex.IsMatch(
                normalizedMerchant ?? rawDescription,
                rule.Pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => false
        };

    private static bool Contains(string? candidate, string pattern) =>
        candidate?.Contains(pattern, StringComparison.OrdinalIgnoreCase) is true;
}
