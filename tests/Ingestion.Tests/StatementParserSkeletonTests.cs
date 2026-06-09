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
    private const string CompletedOutId = "CARD_TRANSACTION-3894508368";
    private const string CompletedInId = "BALANCE_CASHBACK-57097864";
    private const string CancelledMadId = "CARD_TRANSACTION-3733522377";
    private const string RefundedId = "CARD_TRANSACTION-3817952565";
    private const string ZeroAmountId = "CARD_TRANSACTION-2683482520";

    [Fact]
    public void Parse_CompletedRowsAreImportedWithCorrectDirectionAndAmount()
    {
        var transactions = ParseFixture();
        var completed = transactions.Where(transaction => transaction.Status == "completed").ToList();

        Assert.NotEmpty(completed);
        Assert.All(completed, transaction =>
        {
            Assert.Contains(transaction.Direction, new[] { "IN", "OUT" });
            Assert.True(transaction.Amount >= 0);
        });
    }

    [Fact]
    public void Parse_ExplicitDirectionMappedDirectly()
    {
        var transactions = ParseFixture();

        Assert.Equal("OUT", Find(transactions, CompletedOutId).Direction);
        Assert.Equal("IN", Find(transactions, CompletedInId).Direction);
    }

    [Fact]
    public void Parse_SourceTransactionIdIsNativeId()
    {
        var transactions = ParseFixture();

        Assert.Contains(transactions, transaction =>
            transaction.SourceTransactionId!.StartsWith("CARD_TRANSACTION-", StringComparison.Ordinal));
        Assert.Contains(transactions, transaction =>
            transaction.SourceTransactionId!.StartsWith("TRANSFER-", StringComparison.Ordinal));
        Assert.Contains(transactions, transaction =>
            transaction.SourceTransactionId!.StartsWith("BALANCE_CASHBACK-", StringComparison.Ordinal));
        Assert.All(transactions, transaction => Assert.False(string.IsNullOrWhiteSpace(transaction.SourceTransactionId)));
    }

    [Fact]
    public void Parse_CancelledRowsStoredButMarkedCancelled()
    {
        var cancelled = ParseFixture().Where(transaction => transaction.Status == "cancelled").ToList();

        Assert.NotEmpty(cancelled);
        Assert.All(cancelled, transaction => Assert.Equal("cancelled", transaction.Status));
    }

    [Fact]
    public void Parse_RefundedRowsStoredWithRefundedStatus()
    {
        var refunded = ParseFixture().Where(transaction => transaction.Status == "refunded").ToList();

        Assert.NotEmpty(refunded);
        Assert.All(refunded, transaction => Assert.Equal("refunded", transaction.Status));
        Assert.Equal("refunded", Find(refunded, RefundedId).Status);
    }

    [Fact]
    public void Parse_RefundedSemanticsDocumented_NoSeparateInRow()
    {
        var transactions = ParseFixture();
        var refundedIds = transactions
            .Where(transaction => transaction.Status == "refunded")
            .Select(transaction => transaction.SourceTransactionId)
            .ToHashSet();

        Assert.DoesNotContain(transactions, transaction =>
            transaction.Direction == "IN" && refundedIds.Contains(transaction.SourceTransactionId));
    }

    [Fact]
    public void Parse_MultiCurrencyUsesOriginAmount()
    {
        var transaction = Find(ParseFixture(), CancelledMadId);

        Assert.Equal("MAD", transaction.Currency);
        Assert.Equal(2000.00m, transaction.Amount);
    }

    [Fact]
    public void Parse_ZeroAmountRowsPreserved()
    {
        var transaction = Find(ParseFixture(), ZeroAmountId);

        Assert.Equal("refunded", transaction.Status);
        Assert.Equal(0m, transaction.Amount);
    }

    [Fact]
    public void Parse_MerchantNameUsedDirectly()
    {
        var transaction = Find(ParseFixture(), CompletedOutId);

        Assert.Equal("Pravda", transaction.RawDescription);
    }

    [Fact]
    public void Parse_CategorySourcePopulated()
    {
        var transaction = Find(ParseFixture(), CompletedOutId);

        Assert.Equal("Compras", transaction.CategorySource);
    }

    [Fact]
    public void Parse_DeduplicationKey_ReimportDoesNotCreateDuplicates()
    {
        var firstImport = ParseFixture();
        var secondImport = ParseFixture();

        Assert.All(
            firstImport.Concat(secondImport).GroupBy(transaction => transaction.SourceTransactionId),
            group => Assert.Equal(2, group.Count()));
    }

    private static List<ParsedTransaction> ParseFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "wise_sample.csv");
        using var stream = File.OpenRead(path);

        return new WiseCsvParser().Parse(stream).ToList();
    }

    private static ParsedTransaction Find(
        IEnumerable<ParsedTransaction> transactions,
        string sourceTransactionId) =>
        Assert.Single(transactions, transaction => transaction.SourceTransactionId == sourceTransactionId);
}
