using System.Globalization;

using ZiApp.Application.ExchangeRates;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Transactions;

namespace ZiApp.UnitTests.InvestmentLedger;

public sealed class TradeRatePolicyTests
{
    [Theory]
    [InlineData("2025-01-05T23:30:00-05:00", "2025-01-05")]
    [InlineData("2025-01-05T00:30:00+09:00", "2025-01-05")]
    [InlineData("2025-03-30T23:30:00Z", "2025-03-30")]
    public void PreservesBrokerCalendarDateWithoutTimezoneConversion(string timestamp, string expected)
    {
        var trade = Trade(timestamp);
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), TradeRatePolicy.SelectDate(trade));
        Assert.Equal(TradeRatePolicy.Version, TradeRatePolicy.VersionFor(trade));
    }

    [Fact]
    public void OldStoredBrokerDatesAreUsedAsConfirmedByUser()
    {
        var trade = new InvestmentTransaction(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            TradeSide.Buy, new DateTimeOffset(2025, 1, 5, 23, 30, 0, TimeSpan.Zero), 1m, 1m, 0m);
        Assert.Equal(new DateOnly(2025, 1, 5), TradeRatePolicy.SelectDate(trade));
        Assert.Equal(TradeRatePolicy.LegacyVersion, TradeRatePolicy.VersionFor(trade));
    }

    [Theory]
    [InlineData("not-a-time")]
    [InlineData("2025-01-01T12:00:00")]
    [InlineData("2025-01-02T12:00:00Z")]
    public void MalformedOrInconsistentOriginalDoesNotSilentlyFallBack(string original)
    {
        var trade = new InvestmentTransaction(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            TradeSide.Buy, new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), 1m, 1m, 0m, executedAtOriginal: original);
        Assert.Null(TradeRatePolicy.SelectDate(trade));
    }

    [Fact]
    public void RateCanOnlyBeAssignedOnceWithoutChangingInputs()
    {
        var trade = Trade("2025-01-05T23:30:00-05:00");
        DateOnly date = new(2025, 1, 5);
        var rate = new ExchangeRate(Guid.CreateVersion7(), "USD", date, 42.0385m, "Test", DateTimeOffset.UtcNow);
        Guid actor = Guid.CreateVersion7();
        trade.ResolveExchangeRate(rate, date, TradeRatePolicy.Version, actor, DateTimeOffset.UtcNow);
        Assert.Equal(rate.Id, trade.ExchangeRateId);
        Assert.Equal(actor, trade.ExchangeRateResolvedByAccountId);
        Assert.Equal(date, trade.ExchangeRateSelectedDate);
        Assert.Equal(1m, trade.Quantity);
        Assert.Throws<InvalidOperationException>(() => trade.ResolveExchangeRate(rate, date, TradeRatePolicy.Version, actor, DateTimeOffset.UtcNow));
    }

    private static InvestmentTransaction Trade(string timestamp) => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
        TradeSide.Buy, DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).ToUniversalTime(), 1m, 1m, 0m, executedAtOriginal: timestamp);
}