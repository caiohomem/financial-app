using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Ingestion;

public sealed partial class ActivoBankPdfParser : IStatementParser
{
    private const decimal BalanceTolerance = 0.005m;

    public StatementParseResult Parse(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var document = PdfDocument.Open(file);
        var lines = ExtractLines(document);
        var (startDate, endDate) = ParseStatementPeriod(lines);
        var accountIdentifier = ParseAccountIdentifier(lines);

        decimal? previousBalance = null;
        string? pendingDescription = null;
        bool seekingInitialBalance = false;
        var transactions = new List<ParsedTransaction>();

        foreach (var line in lines)
        {
            if (TryParseInitialBalance(line.Text, out var initialBalance))
            {
                previousBalance ??= initialBalance;
                seekingInitialBalance = false;
                continue;
            }

            // Handle PDFs where "SALDO INICIAL" label and its amount are on separate lines
            // (different Y-coordinates in the PDF table layout)
            if (seekingInitialBalance)
            {
                seekingInitialBalance = false;
                var amountMatches = AmountRegex().Matches(line.Text);
                if (amountMatches.Count > 0 && TryParseAmount(amountMatches[^1].Value, out var fallbackBalance))
                {
                    previousBalance ??= fallbackBalance;
                    continue;
                }
            }
            else if (Normalize(line.Text).Contains("SALDO INICIAL", StringComparison.Ordinal))
            {
                seekingInitialBalance = true;
                continue;
            }

            if (TryGetDescriptionContinuation(line.Text, out var continuation))
            {
                pendingDescription = continuation;
                continue;
            }

            if (previousBalance is null ||
                IsControlLine(line.Text) ||
                !TryParseTransactionLine(line.Text, out var parsedLine))
            {
                continue;
            }

            var rawDescription = string.IsNullOrWhiteSpace(parsedLine.RawDescription)
                ? pendingDescription
                : parsedLine.RawDescription;
            pendingDescription = null;

            if (string.IsNullOrWhiteSpace(rawDescription))
            {
                continue;
            }

            var delta = parsedLine.RunningBalance - previousBalance.Value;
            if (Math.Abs(delta) < BalanceTolerance)
            {
                continue;
            }

            transactions.Add(new ParsedTransaction(
                rawDescription,
                MerchantNormalizer.Normalize(rawDescription),
                Math.Abs(delta),
                delta > 0 ? "IN" : "OUT",
                ResolveDate(parsedLine.BookingDate, startDate, endDate),
                ResolveDate(parsedLine.ValueDate, startDate, endDate),
                "EUR",
                parsedLine.RunningBalance,
                null,
                "completed",
                null));

            previousBalance = parsedLine.RunningBalance;
        }

        return new StatementParseResult(accountIdentifier, transactions);
    }

    internal static DateOnly ResolveDate(string monthAndDay, DateOnly startDate, DateOnly endDate)
    {
        var parts = monthAndDay.Split('.');
        var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var day = int.Parse(parts[1], CultureInfo.InvariantCulture);

        var year = startDate.Year == endDate.Year || month >= startDate.Month
            ? startDate.Year
            : endDate.Year;

        return new DateOnly(year, month, day);
    }

    private static IReadOnlyList<PdfLine> ExtractLines(PdfDocument document)
    {
        var lines = new List<PdfLine>();

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords()
                .OrderByDescending(word => word.BoundingBox.Bottom)
                .ThenBy(word => word.BoundingBox.Left)
                .ToList();

            var pageLines = new List<List<Word>>();
            foreach (var word in words)
            {
                var line = pageLines.FirstOrDefault(
                    candidate => Math.Abs((double)candidate[0].BoundingBox.Bottom -
                                          word.BoundingBox.Bottom) <= 2d);

                if (line is null)
                {
                    pageLines.Add([word]);
                }
                else
                {
                    line.Add(word);
                }
            }

            lines.AddRange(pageLines
                .OrderByDescending(line => (double)line[0].BoundingBox.Bottom)
                .Select(line => new PdfLine(
                    string.Join(" ", line
                        .OrderBy(word => word.BoundingBox.Left)
                        .Select(word => word.Text)))));
        }

        return lines;
    }

    private static (DateOnly StartDate, DateOnly EndDate) ParseStatementPeriod(
        IReadOnlyList<PdfLine> lines)
    {
        var text = string.Join(" ", lines.Select(line => line.Text));
        var match = StatementPeriodRegex().Match(text);

        if (!match.Success)
        {
            throw new InvalidDataException("ActivoBank statement period was not found.");
        }

        return (
            DateOnly.ParseExact(match.Groups["start"].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(match.Groups["end"].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture));
    }

    private static string ParseAccountIdentifier(IReadOnlyList<PdfLine> lines)
    {
        var text = string.Join(" ", lines.Select(line => line.Text));
        var ibanMatch = IbanRegex().Match(text);
        if (ibanMatch.Success)
        {
            return Regex.Replace(ibanMatch.Value, @"\s+", string.Empty);
        }

        var depositMatch = DepositNumberRegex().Match(text);
        return depositMatch.Success
            ? Regex.Replace(depositMatch.Groups["number"].Value, @"\s+", string.Empty)
            : "activobank-default";
    }

    private static bool TryParseInitialBalance(string line, out decimal balance)
    {
        balance = default;
        if (!Normalize(line).Contains("SALDO INICIAL", StringComparison.Ordinal))
        {
            return false;
        }

        var matches = AmountRegex().Matches(line);
        return matches.Count > 0 && TryParseAmount(matches[^1].Value, out balance);
    }

    private static bool TryParseTransactionLine(string line, out ParsedLine parsedLine)
    {
        parsedLine = default;
        var transactionMatch = TransactionLineRegex().Match(line);
        if (!transactionMatch.Success)
        {
            return false;
        }

        var bookingStr = transactionMatch.Groups["booking"].Value;
        var valueStr = transactionMatch.Groups["value"].Value;

        // Reject regex matches where a decimal amount was mistaken for a date (e.g. "51.67" → month=51)
        if (!IsValidMonthDay(bookingStr) || !IsValidMonthDay(valueStr))
        {
            return false;
        }

        var body = transactionMatch.Groups["body"].Value;
        var amounts = AmountRegex().Matches(body);
        if (amounts.Count < 2 ||
            !TryParseAmount(amounts[^1].Value, out var runningBalance))
        {
            return false;
        }

        var rawDescription = body[..amounts[^2].Index].Trim();
        parsedLine = new ParsedLine(
            bookingStr,
            valueStr,
            rawDescription,
            runningBalance);

        return true;
    }

    private static bool TryGetDescriptionContinuation(string line, out string description)
    {
        description = string.Empty;
        if (TransactionLineRegex().IsMatch(line) || IsControlLine(line))
        {
            return false;
        }

        var markerIndex = DescriptionMarkers
            .Select(marker => line.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        if (markerIndex < 0)
        {
            return false;
        }

        description = line[markerIndex..].Trim();
        return true;
    }

    private static bool IsValidMonthDay(string monthDay)
    {
        var parts = monthDay.Split('.');
        return parts.Length == 2
            && int.TryParse(parts[0], out var month) && month >= 1 && month <= 12
            && int.TryParse(parts[1], out var day) && day >= 1 && day <= 31;
    }

    private static bool TryParseAmount(string value, out decimal amount) =>
        decimal.TryParse(
            value.Replace(" ", string.Empty, StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);

    private static bool IsControlLine(string line)
    {
        var normalized = Normalize(line);
        return ControlPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)) ||
               (normalized.Contains("DATA LANC", StringComparison.Ordinal) &&
                normalized.Contains("SALDO", StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
    }

    private static readonly string[] ControlPrefixes =
    [
        "A TRANSPORTAR",
        "TRANSPORTE",
        "SALDO INICIAL",
        "SALDO FINAL",
        "SALDO DISPONIVEL",
        "MENSAGEM",
        "RESUMO DAS CONTAS",
        "CARTEIRA DE SEGUROS"
    ];

    private static readonly string[] DescriptionMarkers =
    [
        "COMPRA ",
        "CRED. ",
        "CUSTO ",
        "COMISSAO ",
        "DD ",
        "IMPOSTO ",
        "LEV ",
        "PAG ",
        "PAGSERV ",
        "TRF",
        "TRANSFERENCIA ",
        "VIS "
    ];

    private readonly record struct PdfLine(string Text);

    private readonly record struct ParsedLine(
        string BookingDate,
        string ValueDate,
        string RawDescription,
        decimal RunningBalance);

    [GeneratedRegex(@"EXTRATO\s+DE\s+(?<start>\d{4}/\d{2}/\d{2})\s+A\s+(?<end>\d{4}/\d{2}/\d{2})",
        RegexOptions.IgnoreCase)]
    private static partial Regex StatementPeriodRegex();

    [GeneratedRegex(
        @"(?<booking>\d{1,2}\.\d{2})\s+(?<value>\d{1,2}\.\d{2})\s+(?<body>.+?)\s*$")]
    private static partial Regex TransactionLineRegex();

    [GeneratedRegex(@"(?<![\d.])[+-]?\s*(?:\d{1,3}(?:\s+\d{3})+|\d+)\.\d{2}(?!\d)")]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"PT\s*50(?:\s*\d){21}", RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(
        @"DEP[ÓO]SITO\s+A\s+ORDEM\s*:?\s*(?<number>\d{6,})",
        RegexOptions.IgnoreCase)]
    private static partial Regex DepositNumberRegex();
}
