using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Ingestion;

public sealed class WiseCsvParser : IStatementParser
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public IEnumerable<ParsedTransaction> Parse(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var reader = new StreamReader(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ","
        };
        using var csv = new CsvReader(reader, configuration);

        csv.Context.RegisterClassMap<WiseRowMap>();

        foreach (var row in csv.GetRecords<WiseRow>())
        {
            var bookingDate = ParseBookingDate(row.CompletedAt, row.CreatedAt);

            // Wise's fixture marks the original OUT row as REFUNDED and has no separate IN credit row.
            var status = row.Status.ToLowerInvariant();

            yield return new ParsedTransaction(
                row.BeneficiaryName,
                row.SourceAmount,
                row.Direction,
                bookingDate,
                ValueDate: null,
                row.SourceCurrency,
                RunningBalance: null,
                row.TransferNumber,
                status,
                row.Category);
        }
    }

    private static DateOnly ParseBookingDate(string? completedAt, string createdAt)
    {
        var value = string.IsNullOrWhiteSpace(completedAt) ? createdAt : completedAt;
        var dateTime = DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
        return DateOnly.FromDateTime(dateTime);
    }

    private sealed class WiseRow
    {
        public string TransferNumber { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string? CompletedAt { get; init; }
        public decimal? SourceFeeAmount { get; init; }
        public string? SourceFeeCurrency { get; init; }
        public decimal? DestinationFeeAmount { get; init; }
        public string? DestinationFeeCurrency { get; init; }
        public string? SourceName { get; init; }
        public decimal SourceAmount { get; init; }
        public string SourceCurrency { get; init; } = string.Empty;
        public string BeneficiaryName { get; init; } = string.Empty;
        public decimal DestinationAmount { get; init; }
        public string DestinationCurrency { get; init; } = string.Empty;
        public decimal ExchangeRate { get; init; }
        public string? Reference { get; init; }
        public string? Batch { get; init; }
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
            Map(row => row.SourceFeeAmount).Name("Valor da tarifa de origem");
            Map(row => row.SourceFeeCurrency).Name("Moeda da tarifa de origem");
            Map(row => row.DestinationFeeAmount).Name("Valor da tarifa de destino");
            Map(row => row.DestinationFeeCurrency).Name("Moeda da tarifa de destino");
            Map(row => row.SourceName).Name("Nome de origem");
            Map(row => row.SourceAmount).Name("Valor de origem (tarifas inclusas)");
            Map(row => row.SourceCurrency).Name("Moeda de origem");
            Map(row => row.BeneficiaryName).Name("Nome do beneficiário");
            Map(row => row.DestinationAmount).Name("Valor de destino (tarifas inclusas)");
            Map(row => row.DestinationCurrency).Name("Moeda de destino");
            Map(row => row.ExchangeRate).Name("Taxa de câmbio");
            Map(row => row.Reference).Name("Referência");
            Map(row => row.Batch).Name("Lote");
            Map(row => row.CreatedBy).Name("Criada por");
            Map(row => row.Category).Name("Categoria");
            Map(row => row.Message).Name("Mensagem");
        }
    }
}
