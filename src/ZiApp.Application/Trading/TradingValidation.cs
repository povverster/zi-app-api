using System.Globalization;
using System.Text.RegularExpressions;

namespace ZiApp.Application.Trading;

public static partial class TradingValidation
{
    public static bool IsPageValid(int page, int size) => page > 0 && size is > 0 and <= 100
        && ((long)page - 1) * size <= int.MaxValue;

    public static bool IsTextValid(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Contains('\0', StringComparison.Ordinal);

    public static bool TryAmount(string? text, bool allowZero, out decimal value)
    {
        value = 0m;
        return text is not null && text.Length <= 29 && AmountPattern().IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && (allowZero ? value >= 0m : value > 0m);
    }

    public static bool TryInstant(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || text.Length > 32 || !InstantPattern().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return false;
        }

        value = value.ToUniversalTime();
        return value != DateTimeOffset.MinValue && value <= DateTimeOffset.UtcNow;
    }

    [GeneratedRegex(@"\A[0-9]{1,16}(\.[0-9]{1,12})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,6})?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();
}