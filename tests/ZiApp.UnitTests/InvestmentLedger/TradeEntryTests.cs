using ZiApp.Application.Trading;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Tax;
using ZiApp.Domain.Transactions;

namespace ZiApp.UnitTests.InvestmentLedger;

public sealed class TradeEntryTests
{
    private static readonly Guid InstrumentId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly DateTimeOffset Time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0.000000000001", true)]
    [InlineData("9999999999999999.999999999999", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("1.0000000000001", false)]
    [InlineData("10000000000000000", false)]
    [InlineData("1,25", false)]
    [InlineData("1e2", false)]
    [InlineData(" 1", false)]
    [InlineData(null, false)]
    public void DecimalInputIsExactAndFitsDatabase(string? value, bool valid)
    {
        Assert.Equal(valid, TradingValidation.TryAmount(value, false, out _));
        Assert.True(TradingValidation.TryAmount("0", true, out _));
    }

    [Theory]
    [InlineData("2025-01-01T12:00:00Z", true)]
    [InlineData("2025-01-01T14:00:00+02:00", true)]
    [InlineData("2025-01-01T12:00:00.123456Z", true)]
    [InlineData("2025-01-01T12:00:00.1234567Z", false)]
    [InlineData("2025-01-01T12:00:00", false)]
    [InlineData("2025-02-30T12:00:00Z", false)]
    [InlineData("2999-01-01T12:00:00Z", false)]
    public void TimestampNeedsExplicitOffsetAndDatabasePrecision(string value, bool valid) =>
        Assert.Equal(valid, TradingValidation.TryInstant(value, out _));

    [Fact]
    public void EarlierSalesCannotUseLaterPurchasesAndSymbolsRemainIndependent()
    {
        var buy = Trade(1, TradeSide.Buy, Time, 3m);
        var sell = Trade(2, TradeSide.Sell, Time.AddSeconds(-1), 1m);
        Assert.False(TradeQuantityValidator.CanExecute([buy, sell], []));
        var other = new InvestmentTransaction(Guid.CreateVersion7(), buy.PortfolioId, Guid.CreateVersion7(), null,
            TradeSide.Sell, Time.AddDays(1), 1m, 1m, 0m);
        Assert.False(TradeQuantityValidator.CanExecute([buy, other], []));
    }

    [Fact]
    public void MultipleLotsSplitsAndPartialSalesUseTheSameOrderingAsFifo()
    {
        var first = Trade(1, TradeSide.Buy, Time, 1m);
        var second = Trade(2, TradeSide.Buy, Time.AddDays(1), 2m);
        var split = new StockSplit(Guid.CreateVersion7(), InstrumentId, Time.AddDays(2), 5m, 1m);
        var sell = Trade(3, TradeSide.Sell, Time.AddDays(3), 14.5m);
        Assert.True(TradeQuantityValidator.CanExecute([second, sell, first], [split]));
        Assert.False(TradeQuantityValidator.CanExecute([first, second, sell, Trade(4, TradeSide.Sell, Time.AddDays(4), 1m)], [split]));
    }

    [Fact]
    public void CorrectionKeepsSourceAndOriginalTieBreaker()
    {
        var original = Trade(1, TradeSide.Buy, Time, 1m);
        var sell = Trade(2, TradeSide.Sell, Time, 1m);
        var replacement = new InvestmentTransaction(Guid.CreateVersion7(), original.PortfolioId, InstrumentId, null,
            TradeSide.Buy, Time, 1m, 20m, 1m, fifoOrderId: original.FifoOrderId);
        original.Supersede();
        Assert.Throws<InvalidOperationException>(original.Supersede);
        Assert.True(TradeQuantityValidator.CanExecute([original, sell, replacement], []));
        Assert.Equal(10m, original.UnitPriceUsd);
        var result = FifoRealizedGainCalculator.Calculate(
            [new PurchaseTaxLot(replacement.Id.ToString(), Time, 1m, 20m, 1m, 40m, replacement.FifoOrderId.ToString())],
            [new SaleTaxTransaction(sell.Id.ToString(), Time, 1m, 30m, 1m, 40m, sell.FifoOrderId.ToString())]);
        Assert.Equal(replacement.Id.ToString(), Assert.Single(result.Matches).PurchaseLotId);
        Assert.Equal(8m, result.ProfitUsd);
    }

    [Fact]
    public void AuditRequiresDifferentTradeIdsAndReason()
    {
        Guid id = Guid.CreateVersion7();
        Assert.Throws<ArgumentException>(() => new TradeCorrection(Guid.CreateVersion7(), id, id,
            Guid.CreateVersion7(), "Correction", Time));
        Assert.Throws<ArgumentException>(() => new TradeCorrection(Guid.CreateVersion7(), id, Guid.CreateVersion7(),
            Guid.CreateVersion7(), "  ", Time));
    }

    private static InvestmentTransaction Trade(int id, TradeSide side, DateTimeOffset time, decimal quantity) =>
        new(new Guid(id, 0, 0, new byte[8]), Guid.Parse("00000000-0000-0000-0000-000000000020"),
            InstrumentId, null, side, time, quantity, 10m, 0m);
}