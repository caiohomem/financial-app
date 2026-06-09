namespace Ingestion;

public interface IStatementParser
{
    StatementParseResult Parse(Stream file);
}
