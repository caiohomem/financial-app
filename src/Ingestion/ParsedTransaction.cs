namespace Ingestion;

public record ParsedTransaction(
    string RawDescription,
    decimal Amount,
    string Direction,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    string Currency,
    decimal? RunningBalance,
    string? SourceTransactionId,
    string Status,
    string? CategorySource);
