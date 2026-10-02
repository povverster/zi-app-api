using ZiApp.Domain.Instruments;
using ZiApp.Domain.Tax;
using ZiApp.Domain.Transactions;

namespace ZiApp.UnitTests.Tax;

public sealed class FifoHoldingsTests
{
    private static readonly DateTimeOffset Time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LegacyCalculatorDoesNotComputeNewOpenLotTotals()
    {
        // v1 never valued unsold lots, so the v2 projection must not add overflow to it.
        var result = FifoRealizedGainCalculator.Calculate([new("buy", Time, 2m, decimal.MaxValue, 0m, 40m)], []);
        Assert.Empty(result.Matches);
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void ReverseSplitDoesNotLoseAWholeUnitToRoundedFactor()
    {
        var result = FifoRealizedGainCalculator.CalculateHoldings(
            [new("buy", Time, 3m, 100m, 1m, 40m)],
            [new("sale", Time.AddDays(2), 1m, 330m, 2m, 41m)],
            [new("split", Time.AddDays(1), 1m, 3m)]);
        var match = Assert.Single(result.Matches);
        Assert.Equal(300m, match.PurchaseCostUsd);
        Assert.Equal(1m, match.PurchaseFeeUsd);
        Assert.Equal(27m, match.ProfitUsd);
        Assert.Equal(1408m, match.ProfitUah);
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void PartialSalesAndRepeatedSplitsPreserveRemainingTotalCostAndFees()
    {
        PurchaseTaxLot[] purchases = [new("buy", Time, 1m, 100m, 1m, 40m)];
        StockSplitEvent[] splits = [new("split", Time.AddDays(1), 3m, 1m), new("reverse", Time.AddDays(3), 1m, 2m)];
        SaleTaxTransaction[] sales = [new("sale1", Time.AddDays(2), 1m, 40m, 1m, 41m)];
        var partial = FifoRealizedGainCalculator.CalculateHoldings(purchases, sales, splits);
        var open = Assert.Single(partial.OpenLots);
        Assert.Equal(1m, open.Quantity);
        Assert.Equal(100m, partial.Matches.Sum(m => m.PurchaseCostUsd) + open.PurchaseCostUsd);
        Assert.Equal(1m, partial.Matches.Sum(m => m.PurchaseFeeUsd) + open.PurchaseFeeUsd);
        var closed = FifoRealizedGainCalculator.CalculateHoldings(purchases,
            sales.Append(new("sale2", Time.AddDays(4), 1m, 75m, 0m, 42m)), splits);
        Assert.Empty(closed.OpenLots);
        Assert.Equal(100m, closed.Matches.Sum(m => m.PurchaseCostUsd));
        Assert.Equal(1m, closed.Matches.Sum(m => m.PurchaseFeeUsd));
        Assert.Equal(13m, closed.ProfitUsd);
    }

    [Fact]
    public void MultipleLotsKeepTheirFifoOrderAndAllocateAllSaleFees()
    {
        var result = FifoRealizedGainCalculator.CalculateHoldings(
            [new("buy2", Time, 2m, 20m, 2m, 41m, "b"), new("buy1", Time, 1m, 10m, 1m, 40m, "a")],
            [new("sale", Time.AddDays(1), 3m, 30m, 1m, 42m)]);
        Assert.Equal("buy1", result.Matches[0].PurchaseLotId);
        Assert.Equal("buy2", result.Matches[1].PurchaseLotId);
        Assert.Equal(1m, result.Matches.Sum(m => m.SaleFeeUsd));
        Assert.Equal(50m, result.Matches.Sum(m => m.PurchaseCostUsd));
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void CorrectedSplitRetainsSameTimestampOrderingKey()
    {
        var result = FifoRealizedGainCalculator.CalculateHoldings(
            [new("buy", Time, 1m, 100m, 1m, 40m, "a")],
            [new("sale", Time, 2m, 60m, 1m, 41m, "c")],
            [new("new-split-record", Time, 2m, 1m, "b")]);
        Assert.Equal(100m, Assert.Single(result.Matches).PurchaseCostUsd);
    }

    [Fact]
    public void QuantityValidatorUsesTheSameReverseSplitArithmetic()
    {
        Guid instrument = Guid.CreateVersion7(), portfolio = Guid.CreateVersion7();
        InvestmentTransaction[] trades =
        [
            new(Guid.CreateVersion7(), portfolio, instrument, null, TradeSide.Buy, Time, 3m, 10m, 0m),
            new(Guid.CreateVersion7(), portfolio, instrument, null, TradeSide.Sell, Time.AddDays(2), 1m, 10m, 0m)
        ];
        var split = new StockSplit(Guid.CreateVersion7(), instrument, Time.AddDays(1), 1m, 3m);
        Assert.True(TradeQuantityValidator.CanExecute(trades, [split]));
        Assert.Equal(0m, TradeQuantityValidator.GetHoldings(trades, [split])[instrument]);
        split.Supersede();
        Assert.Equal(2m, TradeQuantityValidator.GetHoldings(trades, [split])[instrument]);
    }

    [Fact]
    public void ZeroOrOverflowedSplitQuantitiesFailInsteadOfDroppingCost()
    {
        Assert.Throws<OverflowException>(() => SplitQuantity.Apply(0.000000000001m, 0.000000000001m, 9999999999999999m));
        Assert.Throws<OverflowException>(() => SplitQuantity.Apply(decimal.MaxValue, 2m, 1m));
    }

    [Fact]
    public void ClosedLotsAreNotChangedByLaterSplits()
    {
        var result = FifoRealizedGainCalculator.CalculateHoldings([new("buy", Time, 1m, 100m, 1m, 40m)],
            [new("sale", Time.AddDays(1), 1m, 110m, 1m, 41m)], [new("split", Time.AddDays(2), 5m, 1m)]);
        Assert.Equal(1m, Assert.Single(result.Matches).MatchedQuantity);
        Assert.Empty(result.OpenLots);
        Assert.Equal(8m, result.ProfitUsd);
    }
}