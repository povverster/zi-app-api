using Microsoft.EntityFrameworkCore;

using Npgsql;

using ZiApp.Application.ExchangeRates;
using ZiApp.Domain.ExchangeRates;

namespace ZiApp.Infrastructure.Persistence;

public sealed class ExchangeRateRepository(ApplicationDbContext db) : IExchangeRateRepository
{
    public Task<ExchangeRate?> FindAsync(DateOnly effectiveDate, CancellationToken cancellationToken) =>
        db.ExchangeRates.AsNoTracking().SingleOrDefaultAsync(rate => rate.CurrencyCode == "USD"
            && rate.Source == NbuRateSource.Key && rate.EffectiveDate == effectiveDate, cancellationToken);

    public async Task<RateResult<ExchangeRate>> CacheAsync(ExchangeRate rate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rate);
        db.ExchangeRates.Add(rate);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(rate).ReloadAsync(cancellationToken);
            return new(rate);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_exchange_rates_currency_date_source",
        })
        {
            db.Entry(rate).State = EntityState.Detached;
            ExchangeRate? existing = await FindAsync(rate.EffectiveDate, cancellationToken);
            return existing is not null && existing.RateToUah == rate.RateToUah && existing.CalculationDate == rate.CalculationDate
                ? new(existing) : new(null, RateError.RateConflict);
        }
    }
}