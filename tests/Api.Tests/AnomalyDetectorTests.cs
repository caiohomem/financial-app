using Xunit;

namespace Api.Tests;

public class AnomalyDetectorTests
{
    private readonly AnomalyDetector _detector = new();

    [Fact]
    public void Detect_FlagsOneOffLargeTransactionsAndIgnoresRecurringOnes()
    {
        var transactions = CreateHistory();
        var config = new AnomalyDetectionConfig();

        var april = _detector.Detect(transactions, "2026-04", config);
        var may = _detector.Detect(transactions, "2026-05", config);

        Assert.Contains(april, anomaly => anomaly.NormalizedMerchant.Contains("Motosolucao", StringComparison.OrdinalIgnoreCase) &&
                                          anomaly.Amount == 931.36m);
        Assert.Contains(april, anomaly => anomaly.NormalizedMerchant.Contains("Motosolucao", StringComparison.OrdinalIgnoreCase) &&
                                          anomaly.Amount == 357.92m);
        Assert.Contains(may, anomaly => anomaly.NormalizedMerchant.Contains("Alessandra", StringComparison.OrdinalIgnoreCase) &&
                                        anomaly.Amount == 1600m);
        Assert.Contains(may, anomaly => anomaly.NormalizedMerchant.Contains("Reembolso Irs", StringComparison.OrdinalIgnoreCase) &&
                                        anomaly.Amount == 3784.62m);

        Assert.DoesNotContain(april, anomaly => anomaly.NormalizedMerchant.Contains("Una Seguros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may, anomaly => anomaly.NormalizedMerchant.Contains("Una Seguros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april, anomaly => anomaly.NormalizedMerchant.Contains("Isabel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may, anomaly => anomaly.NormalizedMerchant.Contains("Isabel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april, anomaly => anomaly.NormalizedMerchant.Contains("Vencimento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may, anomaly => anomaly.NormalizedMerchant.Contains("Vencimento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(april, anomaly => anomaly.NormalizedMerchant.Contains("Metropolitano", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(may, anomaly => anomaly.NormalizedMerchant.Contains("Brewery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_UsesColdStartThresholdWhenHistoryIsTooShort()
    {
        var transactions = new List<AnomalyDetector.TransactionRow>
        {
            Tx(1, "Compra Normal", "Compra Normal", 450m, "OUT", "Compras", new DateOnly(2026, 5, 10)),
            Tx(2, "Compra Muito Grande", "Compra Muito Grande", 2500m, "OUT", "Compras", new DateOnly(2026, 5, 12))
        };

        var anomalies = _detector.Detect(transactions, "2026-05", new AnomalyDetectionConfig
        {
            MinHistoryMonths = 2,
            ColdStartAbsoluteThreshold = 2000
        });

        var anomaly = Assert.Single(anomalies);
        Assert.Equal("Compra Muito Grande", anomaly.NormalizedMerchant);
        Assert.Equal(2500m, anomaly.Amount);
    }

    [Fact]
    public void Detect_ChangingMagnitudeMultiplierChangesWhatIsFlagged()
    {
        var transactions = new List<AnomalyDetector.TransactionRow>
        {
            Tx(1, "Restaurante Base 1", "Restaurante Base 1", 100m, "OUT", "Restaurantes", new DateOnly(2026, 4, 2)),
            Tx(2, "Restaurante Base 2", "Restaurante Base 2", 110m, "OUT", "Restaurantes", new DateOnly(2026, 4, 8)),
            Tx(3, "Restaurante Pico", "Restaurante Pico", 250m, "OUT", "Restaurantes", new DateOnly(2026, 5, 4))
        };

        var lowMultiplier = _detector.Detect(transactions, "2026-05", new AnomalyDetectionConfig
        {
            MagnitudeMultiplier = 2.0,
            AbsoluteFloor = 100
        });
        var highMultiplier = _detector.Detect(transactions, "2026-05", new AnomalyDetectionConfig
        {
            MagnitudeMultiplier = 3.0,
            AbsoluteFloor = 100
        });

        Assert.Single(lowMultiplier);
        Assert.Empty(highMultiplier);
    }

    private static List<AnomalyDetector.TransactionRow> CreateHistory() =>
    [
        Tx(1, "TRF. P/O UNA SEGUROS DE VIDA S A 0504179", "Una Seguros De Vida S A 0504179", 952.58m, "IN", "Seguros", new DateOnly(2026, 4, 5)),
        Tx(2, "TRF P/ ISABEL", "Trf P Isabel", 950m, "OUT", "Transferencias", new DateOnly(2026, 4, 6)),
        Tx(3, "VENCIMENTO", "Vencimento", 3000m, "IN", "Salario", new DateOnly(2026, 4, 7)),
        Tx(4, "MOTOSOLUCAO", "Motosolucao", 931.36m, "OUT", "Transportes", new DateOnly(2026, 4, 11)),
        Tx(5, "MOTOSOLUCAO", "Motosolucao", 357.92m, "OUT", "Transportes", new DateOnly(2026, 4, 14)),
        Tx(6, "METROPOLITANO 1.92", "Metropolitano De Lisboa", 1.92m, "OUT", "Transportes", new DateOnly(2026, 4, 18)),
        Tx(7, "A.M.O.BREWERY", "A.M.O.Brewery", 3.50m, "OUT", "Restaurantes", new DateOnly(2026, 4, 20)),
        Tx(8, "TRF. P/O UNA SEGUROS DE VIDA S A 0504179", "Una Seguros De Vida S A 0504179", 952.85m, "IN", "Seguros", new DateOnly(2026, 5, 5)),
        Tx(9, "TRF P/ ISABEL", "Trf P Isabel", 950m, "OUT", "Transferencias", new DateOnly(2026, 5, 6)),
        Tx(10, "VENCIMENTO", "Vencimento", 3000m, "IN", "Salario", new DateOnly(2026, 5, 7)),
        Tx(11, "TRF MB WAY ALESSANDRA", "Trf Mb Way Alessandra", 1600m, "OUT", "Transferencias", new DateOnly(2026, 5, 8)),
        Tx(12, "REEMBOLSO IRS", "Reembolso Irs", 3784.62m, "IN", "Reembolsos", new DateOnly(2026, 5, 9)),
        Tx(13, "A.M.O.BREWERY", "A.M.O.Brewery", 8.40m, "OUT", "Restaurantes", new DateOnly(2026, 5, 10)),
        Tx(14, "CERVEJA CANIL", "Cerveja Canil", 12.50m, "OUT", "Restaurantes", new DateOnly(2026, 5, 11))
    ];

    private static AnomalyDetector.TransactionRow Tx(
        int id,
        string rawDescription,
        string? normalizedMerchant,
        decimal amount,
        string direction,
        string? category,
        DateOnly bookingDate) =>
        new(
            id,
            rawDescription,
            normalizedMerchant,
            amount,
            direction,
            category,
            bookingDate);
}
