using ZiApp.Application.Security;
using ZiApp.Domain.ExchangeRates;

namespace ZiApp.Application.ExchangeRates;

public sealed class ExchangeRateService(ICurrentAccount account, IExchangeRateRepository repository,
    INbuRateClient client, TimeProvider timeProvider)
{
    public async Task<RateResult<ExchangeRateDetails>> GetAsync(DateOnly date, bool fetch,
        CancellationToken cancellationToken)
    {
        if (await account.GetActiveAccountIdAsync(cancellationToken) is null)
        {
            return new(null, RateError.AccountUnavailable);
        }

        var result = await GetOrFetchAsync(date, fetch, cancellationToken);
        return result.Value is null ? new(null, result.Error) : new(ExchangeRateDetails.From(result.Value));
    }

    // Callers must authorize the owning resource before requesting a trade's rate.
    public async Task<RateResult<ExchangeRate>> GetOrFetchAsync(DateOnly date, bool fetch,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv")).DateTime);
        if (date < NbuRateSource.FirstUahDate || date > today)
        {
            return new(null, RateError.InvalidDate);
        }

        ExchangeRate? cached = await repository.FindAsync(date, cancellationToken);
        if (cached is not null)
        {
            return new(cached);
        }

        if (!fetch)
        {
            return new(null, RateError.NotFound);
        }

        var response = await client.FetchAsync(date, cancellationToken);
        return response.Value is null ? response : await repository.CacheAsync(response.Value, cancellationToken);
    }
}