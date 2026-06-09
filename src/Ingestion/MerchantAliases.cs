namespace Ingestion;

public static class MerchantAliases
{
    public static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBER EATS"] = "Uber Eats",
            ["HELP.UBER.COM"] = "Uber Eats",
            ["GLOVO"] = "Glovo",
            ["ENDESA"] = "Endesa",
            ["NOS COMUNICACOES"] = "NOS Comunicacoes"
        };
}
