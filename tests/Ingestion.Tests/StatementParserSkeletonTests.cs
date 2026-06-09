namespace Ingestion.Tests;

public class ActivoBankPdfParserTests
{
    [Theory]
    [InlineData("activo_2026_004.pdf", 117, 1029.03)]
    [InlineData("activo_2026_005.pdf", 136, 1644.82)]
    public void Parse_ReconcilesEveryPrintedRunningBalance(
        string fixture,
        int expectedTransactionCount,
        decimal expectedFinalBalance)
    {
        var transactions = ParseFixture(fixture);

        Assert.NotEmpty(transactions);
        Assert.Equal(expectedTransactionCount, transactions.Count);

        var balance = GetInitialBalance(transactions[0]);
        foreach (var transaction in transactions)
        {
            balance += transaction.Direction == "IN" ? transaction.Amount : -transaction.Amount;
            Assert.Equal(transaction.RunningBalance, balance);
        }

        Assert.Equal(expectedFinalBalance, balance);
    }

    [Fact]
    public void Parse_PreservesContinuityBetweenStatements()
    {
        var april = ParseFixture("activo_2026_004.pdf");
        var may = ParseFixture("activo_2026_005.pdf");

        var aprilFinalBalance = april[^1].RunningBalance;
        var mayInitialBalance = GetInitialBalance(may[0]);

        Assert.Equal(1029.03m, aprilFinalBalance);
        Assert.Equal(aprilFinalBalance, mayInitialBalance);
    }

    [Fact]
    public void Parse_ExtractsAccountIdentifierFromStatementHeader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "activo_2026_004.pdf");
        using var file = File.OpenRead(path);

        var result = new ActivoBankPdfParser().Parse(file);

        Assert.NotEqual("activobank-default", result.AccountIdentifier);
        Assert.False(string.IsNullOrWhiteSpace(result.AccountIdentifier));
    }

    [Fact]
    public void Parse_NormalizesThousandsSeparatedBySpaces()
    {
        var transactions = ParseFixture("activo_2026_005.pdf");

        Assert.Contains(transactions, transaction =>
            transaction.Amount > 999m || transaction.RunningBalance > 999m);
    }

    [Fact]
    public void Parse_DerivesDirectionFromBalanceDelta()
    {
        var transactions = ParseFixture("activo_2026_004.pdf");

        var transaction = Assert.Single(transactions, transaction =>
            transaction.RawDescription.Contains("UNA SEGUROS", StringComparison.Ordinal));

        Assert.Equal("IN", transaction.Direction);
        Assert.Equal("TRF. P/O UNA SEGUROS DE VIDA S A 0504179", transaction.RawDescription);
    }

    [Fact]
    public void Parse_KeepsFeesAndTaxesAsSeparateTransactions()
    {
        var transactions = ParseFixture("activo_2026_004.pdf");

        Assert.Contains(transactions, transaction =>
            transaction.RawDescription == "CUSTO DE SERVICO INTERNACIONAL");
        Assert.Contains(transactions, transaction =>
            transaction.RawDescription == "IMPOSTO DO SELO");
    }

    [Fact]
    public void Parse_ParsesBookingAndValueDatesIndependently()
    {
        var transactions = ParseFixture("activo_2026_005.pdf");

        Assert.Contains(transactions, transaction =>
            transaction.BookingDate == new DateOnly(2026, 5, 18) &&
            transaction.ValueDate == new DateOnly(2026, 5, 16));
        Assert.All(transactions, transaction => Assert.NotNull(transaction.RunningBalance));
    }

    [Theory]
    [InlineData("activo_2026_004.pdf")]
    [InlineData("activo_2026_005.pdf")]
    public void Parse_FiltersControlLines(string fixture)
    {
        var transactions = ParseFixture(fixture);
        string[] controlLines =
        [
            "A TRANSPORTAR", "TRANSPORTE", "SALDO INICIAL", "SALDO FINAL",
            "SALDO DISPONIVEL", "MENSAGEM", "RESUMO DAS CONTAS", "CARTEIRA DE SEGUROS"
        ];

        Assert.DoesNotContain(transactions, transaction =>
            controlLines.Any(control => transaction.RawDescription.Contains(
                control,
                StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("12.31", "2026-12-31")]
    [InlineData("1.01", "2027-01-01")]
    public void ResolveDate_UsesTheCorrectYearForCrossYearStatements(
        string monthAndDay,
        string expected)
    {
        var result = ActivoBankPdfParser.ResolveDate(
            monthAndDay,
            new DateOnly(2026, 12, 1),
            new DateOnly(2027, 1, 2));

        Assert.Equal(DateOnly.Parse(expected), result);
    }

    private static List<ParsedTransaction> ParseFixture(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        using var file = File.OpenRead(path);

        return new ActivoBankPdfParser().Parse(file).Transactions.ToList();
    }

    private static decimal GetInitialBalance(ParsedTransaction firstTransaction)
    {
        Assert.NotNull(firstTransaction.RunningBalance);

        return firstTransaction.Direction == "IN"
            ? firstTransaction.RunningBalance.Value - firstTransaction.Amount
            : firstTransaction.RunningBalance.Value + firstTransaction.Amount;
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
    public void Parse_WiseCsv_ExtractsAccountAndTransactions()
    {
        var result = ParseResult();

        Assert.Equal("Caio Chagas", result.AccountIdentifier);
        Assert.Equal(53, result.Transactions.Count);
        Assert.All(result.Transactions, transaction =>
            Assert.False(string.IsNullOrWhiteSpace(transaction.SourceTransactionId)));
        Assert.Contains(result.Transactions, transaction =>
            transaction.SourceTransactionId == "CARD_TRANSACTION-3894508368" &&
            transaction.RawDescription == "Pravda" &&
            transaction.Status == "completed");
    }

    [Fact]
    public void Parse_ExplicitDirectionAndNativeTransactionIdArePreserved()
    {
        var transactions = ParseResult().Transactions;

        Assert.Equal("OUT", Find(transactions, CompletedOutId).Direction);
        Assert.Equal("IN", Find(transactions, CompletedInId).Direction);
        Assert.All(transactions, transaction =>
            Assert.False(string.IsNullOrWhiteSpace(transaction.SourceTransactionId)));
    }

    [Fact]
    public void Parse_CancelledAndRefundedRowsAreStoredWithTheirStatus()
    {
        var transactions = ParseResult().Transactions;

        Assert.Equal("cancelled", Find(transactions, CancelledMadId).Status);
        Assert.Equal("refunded", Find(transactions, RefundedId).Status);
    }

    [Fact]
    public void Parse_RefundedFixtureHasNoSeparateInCreditRow()
    {
        var transactions = ParseResult().Transactions;
        var refunded = Find(transactions, RefundedId);

        Assert.Equal("OUT", refunded.Direction);
        Assert.DoesNotContain(transactions, transaction =>
            transaction.Direction == "IN" &&
            transaction.SourceTransactionId == refunded.SourceTransactionId);
    }

    [Fact]
    public void Parse_MultiCurrencyUsesOriginAmountAndCurrency()
    {
        var transaction = Find(ParseResult().Transactions, CancelledMadId);

        Assert.Equal("MAD", transaction.Currency);
        Assert.Equal(2000.00m, transaction.Amount);
    }

    [Fact]
    public void Parse_ZeroAmountRowsArePreserved()
    {
        var transaction = Find(ParseResult().Transactions, ZeroAmountId);

        Assert.Equal(0m, transaction.Amount);
        Assert.Equal("refunded", transaction.Status);
    }

    [Fact]
    public void Parse_MerchantAndCategoryAreMapped()
    {
        var transaction = Find(ParseResult().Transactions, CompletedOutId);

        Assert.Equal("Pravda", transaction.RawDescription);
        Assert.Equal("Compras", transaction.CategorySource);
    }

    [Fact]
    public void Parse_ReimportProducesTheSameNativeDeduplicationKeys()
    {
        var firstImport = ParseResult().Transactions;
        var secondImport = ParseResult().Transactions;

        Assert.Equal(
            firstImport.Select(transaction => transaction.SourceTransactionId),
            secondImport.Select(transaction => transaction.SourceTransactionId));
    }

    private static StatementParseResult ParseResult()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "wise_sample.csv");
        using var file = File.OpenRead(path);

        return new WiseCsvParser().Parse(file);
    }

    private static ParsedTransaction Find(
        IEnumerable<ParsedTransaction> transactions,
        string sourceTransactionId) =>
        Assert.Single(transactions, transaction =>
            transaction.SourceTransactionId == sourceTransactionId);
}
