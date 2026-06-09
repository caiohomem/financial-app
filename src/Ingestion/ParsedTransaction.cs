namespace Ingestion;

public record ParsedTransaction(
    string RawDescription,
    string? NormalizedMerchant,
    decimal Amount,
    string Direction,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    string Currency,
    decimal? RunningBalance,
    string? SourceTransactionId,
    string Status,
    string? CategorySource);
