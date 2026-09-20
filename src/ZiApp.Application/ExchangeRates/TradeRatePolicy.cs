using System.Globalization;

using ZiApp.Application.Trading;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.ExchangeRates;

public static class TradeRatePolicy
{
    public const string Version = "broker-recorded-date-nbu-exact-v1";
    public const string LegacyVersion = "broker-stored-date-nbu-exact-v1";

    public static string VersionFor(InvestmentTransaction trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return trade.ExecutedAtOriginal is null ? LegacyVersion : Version;
    }

    public static DateOnly? SelectDate(InvestmentTransaction trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        // The user confirmed older stored broker dates are already correct too.
        if (trade.ExecutedAtOriginal is null)
        {
            return DateOnly.FromDateTime(trade.ExecutedAtUtc.DateTime);
        }

        if (!TradingValidation.TryInstant(trade.ExecutedAtOriginal, out DateTimeOffset utc)
            || utc != trade.ExecutedAtUtc)
        {
            return null;
        }

        // Keep the broker's supplied calendar date; do not substitute UTC or Kyiv dates.
        var original = DateTimeOffset.Parse(trade.ExecutedAtOriginal!, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return DateOnly.FromDateTime(original.DateTime);
    }

    public static RateError? ValidateForResolution(InvestmentTransaction trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (trade.Portfolio.IsArchived) { return RateError.Archived; }
        if (trade.IsSuperseded) { return RateError.Superseded; }
        if (trade.ExchangeRateId is not null && trade.ExchangeRatePolicy != VersionFor(trade)) { return RateError.LegacyRateLocked; }
        if (SelectDate(trade) is null) { return RateError.InvalidOriginalTimestamp; }
        return null;
    }
}