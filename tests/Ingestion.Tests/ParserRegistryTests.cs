namespace Ingestion.Tests;

public class ParserRegistryTests
{
    [Fact]
    public void Resolve_ReturnsParserRegisteredForNormalizedFileExtension()
    {
        var registry = new ParserRegistry();
        var parser = new StubStatementParser();

        registry.Register("PDF", parser);

        Assert.Same(parser, registry.Resolve(".pdf"));
    }

    private sealed class StubStatementParser : IStatementParser
    {
        public IEnumerable<ParsedTransaction> Parse(Stream file) =>
            Enumerable.Empty<ParsedTransaction>();
    }
}
