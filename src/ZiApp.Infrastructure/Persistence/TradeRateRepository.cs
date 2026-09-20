using Microsoft.EntityFrameworkCore;

using ZiApp.Application.ExchangeRates;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence;

public sealed class TradeRateRepository(ApplicationDbContext db) : ITradeRateRepository
{
    public Task<InvestmentTransaction?> FindAsync(Guid ownerId, Guid portfolioId, Guid tradeId, CancellationToken cancellationToken) =>
        db.InvestmentTransactions.AsNoTracking().Include(item => item.Portfolio).Include(item => item.ExchangeRate)
            .SingleOrDefaultAsync(item => item.Id == tradeId && item.PortfolioId == portfolioId
                && item.Portfolio.OwnerAccountId == ownerId, cancellationToken);

    public async Task<RateResult<TradeRateDetails>> ResolveAsync(Guid ownerId, Guid portfolioId, Guid tradeId,
        Guid rateId, DateOnly effectiveDate, DateTimeOffset resolvedAtUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var portfolios = await db.Portfolios.FromSqlInterpolated(
            $"SELECT * FROM portfolios WHERE id = {portfolioId} AND owner_account_id = {ownerId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (portfolios.Count == 0) { return new(null, RateError.NotFound); }
        // The preflight lookup was no-tracking: this must see changes made during the NBU call.
        var trade = await db.InvestmentTransactions.Include(item => item.ExchangeRate)
            .SingleOrDefaultAsync(item => item.Id == tradeId && item.PortfolioId == portfolioId, cancellationToken);
        if (trade is null) { return new(null, RateError.NotFound); }
        RateError? error = TradeRatePolicy.ValidateForResolution(trade);
        if (error is not null) { return new(null, error); }
        if (TradeRatePolicy.SelectDate(trade) != effectiveDate) { return new(null, RateError.RateConflict); }

        if (trade.ExchangeRateId is null)
        {
            var rate = await db.ExchangeRates.SingleOrDefaultAsync(item => item.Id == rateId, cancellationToken);
            if (rate is null || rate.Source != NbuRateSource.Key || rate.CurrencyCode != "USD"
                || rate.EffectiveDate != effectiveDate || rate.ResponseSha256 is null)
            {
                return new(null, RateError.RateConflict);
            }

            trade.ResolveExchangeRate(rate, effectiveDate, TradeRatePolicy.VersionFor(trade), ownerId, resolvedAtUtc);
            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(trade).ReloadAsync(cancellationToken);
            await db.Entry(trade).Reference(item => item.ExchangeRate).LoadAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new(TradeRateDetails.From(trade));
    }
}