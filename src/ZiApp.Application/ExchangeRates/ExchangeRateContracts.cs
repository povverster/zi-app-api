using System.Globalization;

using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.ExchangeRates;

public static class NbuRateSource
{
    public const string Key = "NBU-ExchangeSite-v1";
    public const string AttributionUrl = "https://bank.gov.ua/ua/open-data";
    public static readonly DateOnly FirstUahDate = new(1996, 9, 2);
}

public enum RateError
{
    AccountUnavailable, NotFound, InvalidDate, RateMissing, UpstreamUnavailable, InvalidResponse,
    RateConflict, Archived, Superseded, LegacyRateLocked, InvalidOriginalTimestamp,
}

public sealed record RateResult<T>(T? Value, RateError? Error = null) where T : class;

public sealed record ExchangeRateDetails(Guid Id, string CurrencyCode, DateOnly EffectiveDate,
    string RateToUah, string Source, DateTimeOffset RetrievedAtUtc, DateOnly? CalculationDate,
    string? SourceUrl, string? ResponseSha256, string? AttributionUrl)
{
    public static ExchangeRateDetails From(ExchangeRate value) => new(value.Id, value.CurrencyCode,
        value.EffectiveDate, value.RateToUah.ToString("0.##########", CultureInfo.InvariantCulture),
        value.Source, value.RetrievedAtUtc, value.CalculationDate, value.SourceUrl,
        value.ResponseSha256, value.Source == NbuRateSource.Key ? NbuRateSource.AttributionUrl : null);
}

public interface INbuRateClient
{
    Task<RateResult<ExchangeRate>> FetchAsync(DateOnly effectiveDate, CancellationToken cancellationToken);
}

public interface IExchangeRateRepository
{
    Task<ExchangeRate?> FindAsync(DateOnly effectiveDate, CancellationToken cancellationToken);
    Task<RateResult<ExchangeRate>> CacheAsync(ExchangeRate rate, CancellationToken cancellationToken);
}

public sealed record TradeRateDetails(Guid TradeId, string Status, DateOnly? SelectedDate,
    string? PolicyVersion, DateTimeOffset? ResolvedAtUtc, Guid? ResolvedByAccountId,
    ExchangeRateDetails? Rate, bool IsTaxReady)
{
    public static TradeRateDetails From(InvestmentTransaction trade) => new(trade.Id,
        trade.ExchangeRateId is null ? "Pending" : trade.ExchangeRatePolicy is null ? "LinkedUnverified" : "Resolved",
        trade.ExchangeRateSelectedDate, trade.ExchangeRatePolicy, trade.ExchangeRateResolvedAtUtc,
        trade.ExchangeRateResolvedByAccountId, trade.ExchangeRate is null ? null : ExchangeRateDetails.From(trade.ExchangeRate), false);
}

public interface ITradeRateRepository
{
    Task<InvestmentTransaction?> FindAsync(Guid ownerId, Guid portfolioId, Guid tradeId, CancellationToken cancellationToken);
    Task<RateResult<TradeRateDetails>> ResolveAsync(Guid ownerId, Guid portfolioId, Guid tradeId,
        Guid rateId, DateOnly effectiveDate, DateTimeOffset resolvedAtUtc, CancellationToken cancellationToken);
}