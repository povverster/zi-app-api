using ZiApp.Domain.Tax;

namespace ZiApp.UnitTests.Tax;

public sealed class FifoRealizedGainCalculatorTests
{
    [Fact]
    public void CalculateReproducesIbitWorkbookTotalsAndFifoOrder()
    {
        PurchaseTaxLot[] purchases =
        [
            Purchase("IBIT-B01", 2025, 10, 30, 14, 30, 1, 4m, 61.53m, 2.08m, 42.0115m),
            Purchase("IBIT-B02", 2025, 11, 3, 17, 1, 14, 2m, 59.95m, 2.04m, 41.8924m),
            Purchase("IBIT-B03", 2025, 11, 20, 20, 40, 45, 4m, 48.97m, 2.08m, 42.0948m),
            Purchase("IBIT-B04", 2025, 11, 21, 14, 3, 52, 4m, 47.25m, 2.08m, 42.1549m),
            Purchase("IBIT-B05", 2025, 11, 21, 15, 30, 24, 6m, 47.68m, 2.12m, 42.1549m),
            Purchase("IBIT-B06", 2026, 2, 2, 14, 27, 18, 2m, 44.27m, 2.04m, 42.8113m),
            Purchase("IBIT-B07", 2026, 2, 4, 20, 40, 45, 1m, 41.05m, 2.02m, 43.1928m),
            Purchase("IBIT-B08", 2026, 2, 18, 15, 31, 18, 10m, 38.08m, 2.20m, 43.2577m),
        ];
        SaleTaxTransaction[] sales =
        [
            Sale("IBIT-S01", 2026, 2, 18, 15, 30, 0, 10m, 38.01m, 2.20m, 43.2577m),
        ];

        var result = FifoRealizedGainCalculator.Calculate(purchases, sales);

        Assert.Collection(
            result.Matches,
            match => AssertMatch(match, "IBIT-B01", "IBIT-S01", 4m),
            match => AssertMatch(match, "IBIT-B02", "IBIT-S01", 2m),
            match => AssertMatch(match, "IBIT-B03", "IBIT-S01", 4m));
        AssertClose(-181.800000m, result.GrossDifferenceUsd);
        AssertClose(-7166.046794m, result.GrossDifferenceUah);
        AssertClose(8.400000m, result.ExpensesUsd);
        AssertClose(355.568540m, result.ExpensesUah);
        AssertClose(-190.200000m, result.ProfitUsd);
        AssertClose(-7521.615334m, result.ProfitUah);
    }

    [Fact]
    public void CalculateReproducesTltWorkbookTotalsWithIndependentFxConversion()
    {
        PurchaseTaxLot[] purchases =
        [
            Purchase("TLT-B01", 2024, 7, 25, 16, 30, 0, 1m, 92.08m, 2.02m, 41.2174m),
            Purchase("TLT-B02", 2024, 8, 30, 16, 30, 0, 2m, 97.68m, 2.04m, 41.1901m),
            Purchase("TLT-B03", 2024, 9, 16, 16, 30, 0, 1m, 100.64m, 2.02m, 41.3171m),
            Purchase("TLT-B04", 2024, 9, 20, 16, 30, 0, 1m, 98.98m, 2.02m, 41.4445m),
            Purchase("TLT-B05", 2024, 10, 3, 16, 30, 0, 1m, 97.08m, 2.02m, 41.2755m),
            Purchase("TLT-B06", 2024, 10, 8, 16, 30, 0, 1m, 94.40m, 2.02m, 41.1961m),
            Purchase("TLT-B07", 2024, 10, 11, 16, 30, 0, 1m, 93.65m, 2.02m, 41.2072m),
            Purchase("TLT-B08", 2024, 10, 22, 16, 30, 0, 1m, 92.66m, 2.02m, 41.2833m),
            Purchase("TLT-B09", 2024, 10, 30, 16, 30, 0, 1m, 92.85m, 2.02m, 41.3798m),
            Purchase("TLT-B10", 2024, 11, 6, 16, 30, 0, 1m, 89.65m, 2.02m, 41.4375m),
            Purchase("TLT-B11", 2024, 12, 17, 16, 30, 0, 2m, 90.78m, 2.04m, 41.7403m),
            Purchase("TLT-B12", 2024, 12, 23, 16, 30, 0, 1m, 88.16m, 2.02m, 41.8761m),
            Purchase("TLT-B13", 2025, 1, 6, 16, 30, 0, 1m, 86.93m, 2.02m, 42.0889m),
            Purchase("TLT-B14", 2025, 1, 31, 16, 30, 0, 3m, 88.39m, 2.06m, 41.8242m),
            Purchase("TLT-B15", 2025, 3, 6, 16, 30, 0, 3m, 89.86m, 2.06m, 41.3680m),
            Purchase("TLT-B16", 2025, 4, 9, 16, 30, 0, 3m, 85.75m, 2.06m, 41.1740m),
            Purchase("TLT-B17", 2025, 5, 1, 16, 30, 0, 3m, 89.34m, 0.00m, 41.4706m),
            Purchase("TLT-B18", 2025, 5, 20, 16, 30, 0, 5m, 85.70m, 2.10m, 41.5760m),
            Purchase("TLT-B19", 2025, 6, 2, 16, 30, 0, 3m, 85.21m, 2.06m, 41.5261m),
            Purchase("TLT-B20", 2025, 7, 8, 16, 30, 0, 2m, 85.63m, 2.04m, 41.7975m),
            Purchase("TLT-B21", 2025, 9, 2, 16, 30, 0, 4m, 85.49m, 2.08m, 41.3722m),
        ];
        SaleTaxTransaction[] sales =
        [
            Sale("TLT-S01", 2025, 11, 13, 20, 1, 20, 7m, 89.59m, 2.14m, 42.0377m),
            Sale("TLT-S02", 2025, 11, 20, 20, 40, 45, 7m, 89.36m, 2.14m, 42.0948m),
            Sale("TLT-S03", 2025, 11, 20, 20, 41, 18, 2m, 89.20m, 2.04m, 42.0948m),
            Sale("TLT-S04", 2025, 11, 21, 16, 26, 29, 6m, 89.52m, 2.12m, 42.1549m),
            Sale("TLT-S05", 2025, 12, 3, 15, 31, 0, 19m, 88.89m, 2.38m, 42.3342m),
        ];

        var result = FifoRealizedGainCalculator.Calculate(purchases, sales);

        AssertClose(41m, result.Matches.Sum(match => match.MatchedQuantity));
        AssertClose(-4.290000m, result.GrossDifferenceUsd);
        AssertClose(2490.099310m, result.GrossDifferenceUah);
        AssertClose(51.580000m, result.ExpensesUsd);
        AssertClose(2146.148824m, result.ExpensesUah);
        AssertClose(-55.870000m, result.ProfitUsd);
        AssertClose(343.950486m, result.ProfitUah);
        Assert.True(result.ProfitUsd < 0m);
        Assert.True(result.ProfitUah > 0m);
    }

    [Fact]
    public void CalculateAppliesSplitToOpenLotsWithoutChangingTheirTotalCost()
    {
        PurchaseTaxLot[] purchases =
        [
            Purchase("SPLIT-B01", 2025, 1, 1, 12, 0, 0, 2m, 100m, 2m, 40m),
        ];
        StockSplitEvent[] splits =
        [
            new("SPLIT-E01", Utc(2025, 2, 1, 12, 0, 0), 5m, 1m),
        ];
        SaleTaxTransaction[] sales =
        [
            Sale("SPLIT-S01", 2025, 3, 1, 12, 0, 0, 10m, 22m, 1m, 41m),
        ];

        var result = FifoRealizedGainCalculator.Calculate(purchases, sales, splits);

        var match = Assert.Single(result.Matches);
        AssertClose(10m, match.MatchedQuantity);
        AssertClose(200m, match.PurchaseCostUsd);
        AssertClose(2m, match.PurchaseFeeUsd);
        AssertClose(220m, match.SaleProceedsUsd);
        AssertClose(17m, result.ProfitUsd);
        AssertClose(899m, result.ProfitUah);
    }

    [Fact]
    public void CalculateRejectsSaleThatExceedsAvailableQuantity()
    {
        PurchaseTaxLot[] purchases =
        [
            Purchase("OVER-B01", 2025, 1, 1, 12, 0, 0, 1m, 100m, 0m, 40m),
        ];
        SaleTaxTransaction[] sales =
        [
            Sale("OVER-S01", 2025, 1, 2, 12, 0, 0, 2m, 110m, 0m, 41m),
        ];

        var exception = Assert.Throws<InvalidOperationException>(
            () => FifoRealizedGainCalculator.Calculate(purchases, sales));

        Assert.Contains("exceeds the available FIFO quantity", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertMatch(
        RealizedTaxLotMatch match,
        string purchaseLotId,
        string saleId,
        decimal quantity)
    {
        Assert.Equal(purchaseLotId, match.PurchaseLotId);
        Assert.Equal(saleId, match.SaleId);
        AssertClose(quantity, match.MatchedQuantity);
    }

    private static void AssertClose(decimal expected, decimal actual)
    {
        const decimal tolerance = 0.000001m;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    private static PurchaseTaxLot Purchase(
        string id,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        decimal quantity,
        decimal unitPriceUsd,
        decimal feeUsd,
        decimal usdToUahRate) =>
        new(id, Utc(year, month, day, hour, minute, second), quantity, unitPriceUsd, feeUsd, usdToUahRate);

    private static SaleTaxTransaction Sale(
        string id,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        decimal quantity,
        decimal unitPriceUsd,
        decimal feeUsd,
        decimal usdToUahRate) =>
        new(id, Utc(year, month, day, hour, minute, second), quantity, unitPriceUsd, feeUsd, usdToUahRate);

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}