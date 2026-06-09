namespace Ingestion;

public record StatementParseResult(
    string AccountIdentifier,
    IReadOnlyList<ParsedTransaction> Transactions);
