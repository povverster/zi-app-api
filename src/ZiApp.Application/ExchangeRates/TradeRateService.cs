using ZiApp.Application.Security;

namespace ZiApp.Application.ExchangeRates;

public sealed class TradeRateService(ICurrentAccount account, ITradeRateRepository repository,
    ExchangeRateService rates, TimeProvider timeProvider)
{
    public async Task<RateResult<TradeRateDetails>> GetAsync(Guid portfolioId, Guid tradeId, bool resolve,
        CancellationToken cancellationToken)
    {
        Guid? ownerId = await account.GetActiveAccountIdAsync(cancellationToken);
        if (ownerId is null) { return new(null, RateError.AccountUnavailable); }
        var trade = await repository.FindAsync(ownerId.Value, portfolioId, tradeId, cancellationToken);
        if (trade is null) { return new(null, RateError.NotFound); }
        if (!resolve) { return new(TradeRateDetails.From(trade)); }

        RateError? error = TradeRatePolicy.ValidateForResolution(trade);
        if (error is not null) { return new(null, error); }
        DateOnly date = TradeRatePolicy.SelectDate(trade)!.Value;
        if (trade.ExchangeRateId is not null)
        {
            // Recheck current state under the lock even for an idempotent repeated resolution.
            return await repository.ResolveAsync(ownerId.Value, portfolioId, tradeId,
                trade.ExchangeRateId.Value, date, timeProvider.GetUtcNow(), cancellationToken);
        }

        // Never hold a portfolio lock while waiting for an external network call.
        var rate = await rates.GetOrFetchAsync(date, true, cancellationToken);
        return rate.Value is null ? new(null, rate.Error)
            : await repository.ResolveAsync(ownerId.Value, portfolioId, tradeId,
                rate.Value.Id, date, timeProvider.GetUtcNow(), cancellationToken);
    }
}