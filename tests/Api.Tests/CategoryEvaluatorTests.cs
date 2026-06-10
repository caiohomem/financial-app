using Xunit;

namespace Api.Tests;

public class CategoryEvaluatorTests
{
    [Fact]
    public void Evaluate_PrefersSourceMappingOverRules()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "Glovo", "merchant_eq", 10, 100)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Compras"] = 42
            });

        var result = evaluator.Evaluate("Compras", "Glovo", "GLOVO LISBOA");

        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_MatchesMerchantEqCaseInsensitively()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "Glovo", "merchant_eq", 10, 100)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        var result = evaluator.Evaluate(null, "gLoVo", "glovo lisboa");

        Assert.Equal(10, result);
    }

    [Fact]
    public void Evaluate_ContainsMatchesMerchantAndFallsBackToRawDescription()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "Metropolitano De Lisboa", "contains", 7, 90)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        Assert.Equal(7, evaluator.Evaluate(null, "Metropolitano De Lisboa Contactless", "raw"));
        Assert.Equal(7, evaluator.Evaluate(null, null, "Viagem Metropolitano De Lisboa"));
    }

    [Fact]
    public void Evaluate_RegexMatchesWhenConfigured()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, @"^Bolt\.(Eu|Eur|Euo)", "regex", 4, 90)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        var result = evaluator.Evaluate(null, "Bolt.Eur2605192119 Tallinn Ee", "raw");

        Assert.Equal(4, result);
    }

    [Fact]
    public void Evaluate_UsesFirstMatchingRuleInPriorityOrder()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "Google One", "contains", 6, 100),
                new CategoryEvaluator.RuleRow(2, "Google", "contains", 3, 50)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        var result = evaluator.Evaluate(null, "Google One", "Google One");

        Assert.Equal(6, result);
    }

    [Fact]
    public void Evaluate_ReturnsNullWhenNothingMatches()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "Glovo", "merchant_eq", 10, 100)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        var result = evaluator.Evaluate(null, "Pravda", "Pravda");

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_IsDeterministicForRepeatedInputs()
    {
        var evaluator = new CategoryEvaluator(
            [
                new CategoryEvaluator.RuleRow(1, "A.M.O.Brewery", "contains", 5, 90),
                new CategoryEvaluator.RuleRow(2, "Brewery", "contains", 4, 50)
            ],
            new Dictionary<string, int>(StringComparer.Ordinal));

        var first = evaluator.Evaluate(
            null,
            "A.M.O.Brewery Lisboa Pt Contactless",
            "COMPRA A.M.O.BREWERY");
        var second = evaluator.Evaluate(
            null,
            "A.M.O.Brewery Lisboa Pt Contactless",
            "COMPRA A.M.O.BREWERY");

        Assert.Equal(first, second);
        Assert.Equal(5, first);
    }
}
