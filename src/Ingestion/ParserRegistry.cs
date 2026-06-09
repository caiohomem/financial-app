namespace Ingestion;

public sealed class ParserRegistry
{
    private readonly Dictionary<string, IStatementParser> _parsers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string fileExtension, IStatementParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);

        var normalizedExtension = NormalizeExtension(fileExtension);

        if (!_parsers.TryAdd(normalizedExtension, parser))
        {
            throw new InvalidOperationException(
                $"A statement parser is already registered for '{normalizedExtension}'.");
        }
    }

    public IStatementParser Resolve(string fileExtension)
    {
        var normalizedExtension = NormalizeExtension(fileExtension);

        return _parsers.TryGetValue(normalizedExtension, out var parser)
            ? parser
            : throw new KeyNotFoundException(
                $"No statement parser is registered for '{normalizedExtension}'.");
    }

    private static string NormalizeExtension(string fileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);

        var trimmedExtension = fileExtension.Trim();
        return trimmedExtension.StartsWith('.')
            ? trimmedExtension.ToLowerInvariant()
            : $".{trimmedExtension.ToLowerInvariant()}";
    }
}
