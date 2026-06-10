using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ingestion;

public static partial class MerchantNormalizer
{
    public static string Normalize(string rawDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDescription);

        var normalized = NormalizeForMatching(rawDescription);
        normalized = PrefixRegex().Replace(normalized, string.Empty);
        normalized = SuffixRegex().Replace(normalized, string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        foreach (var alias in MerchantAliases.Map)
        {
            if (normalized.Contains(alias.Key, StringComparison.OrdinalIgnoreCase))
            {
                return alias.Value;
            }
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string NormalizeForMatching(string value)
    {
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
    }

    [GeneratedRegex(
        @"^(COMPRA\s+\d{4}|PAGSERV|DD|PAG|LEV|TRF\.?\s*P/O|TRANSFERENCIA|VIS|CRED\.?|CUSTO)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(
        @"\s+(?:" + CountrySuffixPattern + "|" + CitySuffixPattern + @")$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuffixRegex();

    private const string CountrySuffixPattern = "PT|NL|ES|FR|UK|GB|DE|IE";
    private const string CitySuffixPattern = "LISBOA|PORTO|BRAGA|COIMBRA|FARO|SETUBAL";
}
