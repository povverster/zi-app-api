using System.Globalization;

using ZiApp.Application.Reports;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;

namespace ZiApp.UnitTests.Tax;

public sealed class ConfiguredTaxTests
{
    [Theory]
    [InlineData("10000", "0", "10000", "1800.00", "500.00", "2300.00")]
    [InlineData("-10000", "-10000", "0", "0.00", "0.00", "0.00")]
    [InlineData("0", "0", "0", "0.00", "0.00", "0.00")]
    [InlineData("-73.735112", "-73.735112", "0", "0.00", "0.00", "0.00")]
    [InlineData("-273.673635", "-273.673635", "0", "0.00", "0.00", "0.00")]
    [InlineData("-410.150137", "-410.150137", "0", "0.00", "0.00", "0.00")]
    [InlineData("343.950486", "0", "343.950486", "61.91", "17.20", "79.11")]
    [InlineData("7633.319681", "0", "7633.319681", "1374.00", "381.67", "1755.67")]
    public void SignedSpreadsheetAndSyntheticResultsRemainVisible(string profit, string loss, string basis,
        string investment, string military, string total)
    {
        // BND/BNDX/corrected BXMT/TLT/SCHD subtotals transcribed from the read-only
        // sample audit. This tests rate arithmetic, not SCHD broker-batch reconstruction.
        var result = ConfiguredTaxCalculator.Calculate(decimal.Parse(profit, CultureInfo.InvariantCulture), 18m, 5m);
        Assert.Equal(new(profit, loss, basis, investment, military, total), result);
    }

    [Theory]
    [InlineData("1.005", "100", "1.01")]
    [InlineData("1.0049999999999999999999999999", "100", "1.00")]
    [InlineData("1.0050000000000000000000000001", "100", "1.01")]
    [InlineData("20.1", "5", "1.01")]
    [InlineData("10000", "1.5", "150.00")]
    [InlineData("10000", "0.0001", "0.01")]
    [InlineData("10000", "0", "0.00")]
    public void FinalTaxesRoundOnceWithoutIntermediatePrecisionLoss(string profit, string rate, string expected)
    {
        var result = ConfiguredTaxCalculator.Calculate(decimal.Parse(profit, CultureInfo.InvariantCulture),
            decimal.Parse(rate, CultureInfo.InvariantCulture), 0m);
        Assert.Equal(expected, result.InvestmentTaxUah);
        Assert.Equal(expected, result.TotalTaxUah);
    }

    [Fact]
    public void TotalIsTheSumOfIndividuallyRoundedTaxes()
    {
        var result = ConfiguredTaxCalculator.Calculate(0.1m, 5m, 5m);
        Assert.Equal("0.01", result.InvestmentTaxUah);
        Assert.Equal("0.01", result.MilitaryTaxUah);
        Assert.Equal("0.02", result.TotalTaxUah);
    }

    [Fact]
    public void LargeAmountsAvoidMultiplicationOverflowButRejectTotalOverflow()
    {
        Assert.Equal("79228162514264337593543950335.00",
            ConfiguredTaxCalculator.Calculate(decimal.MaxValue, 100m, 0m).TotalTaxUah);
        Assert.Throws<OverflowException>(() => ConfiguredTaxCalculator.Calculate(decimal.MaxValue, 100m, 100m));
        Assert.Equal("-79228162514264337593543950335",
            ConfiguredTaxCalculator.Calculate(decimal.MinValue, 18m, 5m).LossUah);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.0001")]
    [InlineData("18.12345")]
    [InlineData("18.00000")]
    [InlineData("1e1")]
    [InlineData("18,5")]
    [InlineData("NaN")]
    [InlineData(" 18")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidRateStringsAreRejected(string? value) =>
        Assert.False(ConfiguredTaxSnapshot.ValidSettings(new(value!, "5", "9")));

    [Theory]
    [InlineData("0")]
    [InlineData("18")]
    [InlineData("1.5")]
    [InlineData("0.0001")]
    [InlineData("100.0000")]
    public void ExactPercentagesAreAccepted(string value) =>
        Assert.True(ConfiguredTaxSnapshot.ValidSettings(new(value, value, value)));

    [Fact]
    public void DomainRejectsInvalidRatesAndCreatesYearSpecificRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfiguredTaxCalculator.Calculate(100m, -1m, 5m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfiguredTaxCalculator.Calculate(100m, 18m, 101m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfiguredTaxCalculator.Calculate(100m, 18.00001m, 5m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaxSettingsRevision(Guid.CreateVersion7(), Guid.CreateVersion7(),
            1999, 18m, 5m, 9m, DateTimeOffset.UtcNow));
    }
}
