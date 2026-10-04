using System.Globalization;
using System.Numerics;

namespace ZiApp.Domain.Tax;

public sealed record ConfiguredTaxAmounts(string NetProfitUah, string LossUah, string TaxBaseUah,
    string InvestmentTaxUah, string MilitaryTaxUah, string TotalTaxUah);

/// <summary>User-configured arithmetic, not a statutory tax-policy implementation.</summary>
public static class ConfiguredTaxCalculator
{
    public const string Version = "positive-annual-net-configured-rates-v1";
    public const string RoundingPolicy = "each-tax-2dp-half-away-from-zero-v1";

    public static ConfiguredTaxAmounts Calculate(decimal netProfitUah, decimal investmentPercent, decimal militaryPercent)
    {
        ValidateRate(investmentPercent);
        ValidateRate(militaryPercent);
        decimal basis = Math.Max(netProfitUah, 0m);
        BigInteger investmentCents = TaxCents(basis, investmentPercent);
        BigInteger militaryCents = TaxCents(basis, militaryPercent);
        return new(Format(netProfitUah), Format(Math.Min(netProfitUah, 0m)), Format(basis), Money(investmentCents), Money(militaryCents),
            Money(investmentCents + militaryCents));
    }

    public static void ValidateRate(decimal value)
    {
        if (value is < 0m or > 100m || decimal.Round(value, 4) != value)
        { throw new ArgumentOutOfRangeException(nameof(value), "Use a percentage from 0 to 100 with at most four fractional places."); }
    }

    // Work with exact decimal coefficients. Multiplying decimal values first could
    // overflow or round a value across the half-cent boundary before final rounding.
    private static BigInteger TaxCents(decimal basis, decimal percent)
    {
        var (amount, amountScale) = Parts(basis);
        var (rate, rateScale) = Parts(percent);
        BigInteger divisor = BigInteger.Pow(10, amountScale + rateScale);
        BigInteger cents = BigInteger.DivRem(amount * rate, divisor, out BigInteger remainder);
        return remainder * 2 >= divisor ? cents + 1 : cents;
    }

    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        return ((BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32)
            + ((BigInteger)(uint)bits[2] << 64), (bits[3] >> 16) & 255);
    }

    private static string Money(BigInteger cents)
    {
        string text = cents.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        string result = text.Insert(text.Length - 2, ".");
        // Reject a total outside the supported decimal range, rather than silently
        // returning a rounded/overflowed monetary amount.
        if (cents > ((BigInteger.One << 96) - 1) * 100) { throw new OverflowException("Tax total exceeds decimal range."); }
        return result;
    }

    private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
