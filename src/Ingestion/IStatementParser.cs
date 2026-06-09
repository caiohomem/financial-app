namespace Ingestion;

public interface IStatementParser
{
    IEnumerable<ParsedTransaction> Parse(Stream file);
}
