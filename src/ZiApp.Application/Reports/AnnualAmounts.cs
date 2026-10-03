using System.Globalization;
using System.Numerics;

using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;

namespace ZiApp.Application.Reports;

public static class AnnualAmounts
{
    public static bool IsInput(string? text, bool allowNegative = false)
    {
        if (text is null) { return false; }
        string magnitude = allowNegative && text.StartsWith('-') ? text[1..] : text;
        return TradingValidation.TryAmount(magnitude, true, out _);
    }

    // Decimal addition can silently discard low-order digits at its precision limit.
    // Accumulate coefficients exactly, then reject an unrepresentable result instead.
    public static string Sum(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        BigInteger total = BigInteger.Zero;
        foreach (string value in values)
        {
            if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal number) || LedgerDecimal.Format(number) != value)
            { throw new AnnualSummaryValidationException(AnnualSummaryError.InvalidSource); }
            int[] bits = decimal.GetBits(number);
            int scale = (bits[3] >> 16) & 255;
            BigInteger coefficient = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32)
                + ((BigInteger)(uint)bits[2] << 64);
            if (bits[3] < 0) { coefficient = -coefficient; }
            total += coefficient * BigInteger.Pow(10, 28 - scale);
        }
        int resultScale = 28;
        while (resultScale > 0 && total % 10 == 0) { total /= 10; resultScale--; }
        bool negative = total.Sign < 0;
        BigInteger magnitude = BigInteger.Abs(total);
        if (magnitude > (BigInteger.One << 96) - 1)
        { throw new AnnualSummaryValidationException(AnnualSummaryError.ArithmeticNotRepresentable); }
        decimal result = new((int)(uint)(magnitude & uint.MaxValue),
            (int)(uint)((magnitude >> 32) & uint.MaxValue),
            (int)(uint)((magnitude >> 64) & uint.MaxValue), negative, (byte)resultScale);
        return LedgerDecimal.Format(result);
    }

    public static string CanonicalInput(string text) =>
        LedgerDecimal.Format(decimal.Parse(text, CultureInfo.InvariantCulture));
}
