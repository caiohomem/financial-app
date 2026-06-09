namespace Ingestion.Tests;

public class ActivoBankPdfParserTests
{
    [Theory]
    [InlineData("activo_2026_004.pdf")]
    [InlineData("activo_2026_005.pdf")]
    public void Parse_ActivoBankPdf_FixtureExists(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);

        Assert.True(File.Exists(path), $"Fixture not found: {path}");
        // TODO: Parse this fixture with ActivoBankPdfParser when it is implemented.
    }
}

public class WiseCsvParserTests
{
    [Fact]
    public void Parse_WiseCsv_FixtureExists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "wise_sample.csv");

        Assert.True(File.Exists(path), $"Fixture not found: {path}");
        // TODO: Parse this fixture with WiseCsvParser when it is implemented.
    }
}
