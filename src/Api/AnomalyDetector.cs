using System.Globalization;

public sealed class AnomalyDetector
{
    public const string DetectionMethod = "recurrence-filter+magnitude-outlier";

    public IReadOnlyList<AnomalyResult> Detect(
        IReadOnlyList<TransactionRow> allTransactions,
        string targetMonth,
        AnomalyDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(allTransactions);
        ArgumentNullException.ThrowIfNull(config);

        if (!TryParseMonth(targetMonth, out var targetMonthStart))
        {
            throw new ArgumentException("Expected month in YYYY-MM format.", nameof(targetMonth));
        }

        var distinctMonths = allTransactions
            .Select(transaction => ToMonthKey(transaction.BookingDate))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var isColdStart = distinctMonths < config.MinHistoryMonths;
        var targetTransactions = allTransactions
            .Where(transaction => IsInMonth(transaction.BookingDate, targetMonthStart))
            .OrderBy(transaction => transaction.BookingDate)
            .ThenBy(transaction => transaction.Id)
            .ToList();

        if (targetTransactions.Count == 0)
        {
            return [];
        }

        return isColdStart
            ? DetectColdStart(targetTransactions, config)
            : DetectWithHistory(allTransactions, targetTransactions, targetMonthStart, config);
    }

    internal static bool TryParseMonth(string value, out DateOnly monthStart)
    {
        if (DateOnly.TryParseExact(
                $"{value}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out monthStart))
        {
            return true;
        }

        monthStart = default;
        return false;
    }

    private static IReadOnlyList<AnomalyResult> DetectColdStart(
        IReadOnlyList<TransactionRow> targetTransactions,
        AnomalyDetectionConfig config)
    {
        var threshold = ToDecimal(config.ColdStartAbsoluteThreshold);

        return targetTransactions
            .Where(transaction => transaction.Amount > threshold)
            .Select(transaction => new AnomalyResult(
                transaction.Id,
                transaction.NormalizedMerchant ?? transaction.RawDescription,
                transaction.RawDescription,
                transaction.Amount,
                transaction.Direction,
                transaction.CategoryName,
                transaction.BookingDate,
                transaction.Amount / threshold,
                threshold,
                threshold))
            .ToList();
    }

    private static IReadOnlyList<AnomalyResult> DetectWithHistory(
        IReadOnlyList<TransactionRow> allTransactions,
        IReadOnlyList<TransactionRow> targetTransactions,
        DateOnly targetMonthStart,
        AnomalyDetectionConfig config)
    {
        var results = new List<AnomalyResult>();
        var magnitudeMultiplier = ToDecimal(config.MagnitudeMultiplier);
        var absoluteFloor = ToDecimal(config.AbsoluteFloor);

        foreach (var transaction in targetTransactions)
        {
            if (IsRecurring(allTransactions, transaction, config))
            {
                continue;
            }

            var categoryMedian = GetCategoryMedian(allTransactions, transaction, targetMonthStart, config);
            var threshold = Math.Max(categoryMedian * magnitudeMultiplier, absoluteFloor);
            if (transaction.Amount <= threshold)
            {
                continue;
            }

            var comparisonBaseline = categoryMedian > 0m ? categoryMedian : threshold;
            results.Add(new AnomalyResult(
                transaction.Id,
                transaction.NormalizedMerchant ?? transaction.RawDescription,
                transaction.RawDescription,
                transaction.Amount,
                transaction.Direction,
                transaction.CategoryName,
                transaction.BookingDate,
                comparisonBaseline > 0m ? transaction.Amount / comparisonBaseline : 1m,
                categoryMedian,
                threshold));
        }

        return results;
    }

    private static bool IsRecurring(
        IReadOnlyList<TransactionRow> allTransactions,
        TransactionRow candidate,
        AnomalyDetectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(candidate.NormalizedMerchant))
        {
            return false;
        }

        var tolerancePct = ToDecimal(config.RecurrenceTolerancePct);
        var lowerBound = candidate.Amount * (1m - tolerancePct);
        var upperBound = candidate.Amount * (1m + tolerancePct);
        var matchingMonths = allTransactions
            .Where(transaction =>
                transaction.Direction == candidate.Direction &&
                string.Equals(
                    transaction.NormalizedMerchant,
                    candidate.NormalizedMerchant,
                    StringComparison.OrdinalIgnoreCase) &&
                transaction.Amount >= lowerBound &&
                transaction.Amount <= upperBound)
            .Select(transaction => ToMonthKey(transaction.BookingDate))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return matchingMonths >= config.RecurrenceMinMonths;
    }

    private static decimal GetCategoryMedian(
        IReadOnlyList<TransactionRow> allTransactions,
        TransactionRow candidate,
        DateOnly targetMonthStart,
        AnomalyDetectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(candidate.CategoryName))
        {
            return 0m;
        }

        var values = allTransactions
            .Where(transaction =>
                transaction.Direction == candidate.Direction &&
                !IsInMonth(transaction.BookingDate, targetMonthStart) &&
                !IsRecurring(allTransactions, transaction, config) &&
                string.Equals(
                    transaction.CategoryName,
                    candidate.CategoryName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(transaction => transaction.Amount)
            .OrderBy(amount => amount)
            .ToList();

        if (values.Count == 0)
        {
            return 0m;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }

    private static bool IsInMonth(DateOnly date, DateOnly monthStart) =>
        date.Year == monthStart.Year && date.Month == monthStart.Month;

    private static string ToMonthKey(DateOnly date) =>
        $"{date.Year:D4}-{date.Month:D2}";

    private static decimal ToDecimal(double value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    public sealed record TransactionRow(
        int Id,
        string RawDescription,
        string? NormalizedMerchant,
        decimal Amount,
        string Direction,
        string? CategoryName,
        DateOnly BookingDate);

    public sealed record AnomalyResult(
        int TransactionId,
        string NormalizedMerchant,
        string RawDescription,
        decimal Amount,
        string Direction,
        string? Category,
        DateOnly BookingDate,
        decimal DeviationFactor,
        decimal CategoryMedian,
        decimal Threshold);
}
