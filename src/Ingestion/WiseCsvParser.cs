using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Ingestion;

public sealed class WiseCsvParser : IStatementParser
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public StatementParseResult Parse(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var reader = new StreamReader(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ","
        });

        csv.Context.RegisterClassMap<WiseRowMap>();
        var rows = csv.GetRecords<WiseRow>().ToList();
        var accountIdentifier = rows
            .Select(row => row.CreatedBy)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? rows.Select(row => row.SourceName)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "wise-default";

        var transactions = rows.Select(row => new ParsedTransaction(
            string.IsNullOrWhiteSpace(row.BeneficiaryName)
                ? row.Reference ?? row.Message ?? row.TransferNumber
                : row.BeneficiaryName,
            row.SourceAmount,
            row.Direction,
            ParseBookingDate(row.CompletedAt, row.CreatedAt),
            ValueDate: null,
            row.SourceCurrency,
            RunningBalance: null,
            row.TransferNumber,
            row.Status.ToLowerInvariant(),
            row.Category)).ToList();

        return new StatementParseResult(accountIdentifier.Trim(), transactions);
    }

    private static DateOnly ParseBookingDate(string? completedAt, string createdAt)
    {
        var value = string.IsNullOrWhiteSpace(completedAt) ? createdAt : completedAt;
        return DateOnly.FromDateTime(
            DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture));
    }

    private sealed class WiseRow
    {
        public string TransferNumber { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string? CompletedAt { get; init; }
        public string? SourceName { get; init; }
        public decimal SourceAmount { get; init; }
        public string SourceCurrency { get; init; } = string.Empty;
        public string BeneficiaryName { get; init; } = string.Empty;
        public string? Reference { get; init; }
        public string? CreatedBy { get; init; }
        public string? Category { get; init; }
        public string? Message { get; init; }
    }

    private sealed class WiseRowMap : ClassMap<WiseRow>
    {
        public WiseRowMap()
        {
            Map(row => row.TransferNumber).Name("Número da transferência");
            Map(row => row.Status).Name("Situação");
            Map(row => row.Direction).Name("Direção");
            Map(row => row.CreatedAt).Name("Criada em");
            Map(row => row.CompletedAt).Name("Concluída em");
            Map(row => row.SourceName).Name("Nome de origem");
            Map(row => row.SourceAmount).Name("Valor de origem (tarifas inclusas)");
            Map(row => row.SourceCurrency).Name("Moeda de origem");
            Map(row => row.BeneficiaryName).Name("Nome do beneficiário");
            Map(row => row.Reference).Name("Referência");
            Map(row => row.CreatedBy).Name("Criada por");
            Map(row => row.Category).Name("Categoria");
            Map(row => row.Message).Name("Mensagem");
        }
    }
}
