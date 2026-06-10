public sealed class AnomalyDetectionConfig
{
    public const string SectionName = "AnomalyDetection";

    public int RecurrenceMinMonths { get; init; } = 2;
    public double RecurrenceTolerancePct { get; init; } = 0.05;
    public double MagnitudeMultiplier { get; init; } = 2.0;
    public double AbsoluteFloor { get; init; } = 300.0;
    public int MinHistoryMonths { get; init; } = 2;
    public double ColdStartAbsoluteThreshold { get; init; } = 2000.0;
}
