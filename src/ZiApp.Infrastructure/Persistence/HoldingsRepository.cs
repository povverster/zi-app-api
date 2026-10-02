using System.Data;

using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Ledger;

namespace ZiApp.Infrastructure.Persistence;

public sealed class HoldingsRepository(ApplicationDbContext db) : IHoldingsRepository
{
    public async Task<HoldingsLedger?> LoadAsync(Guid ownerId, Guid portfolioId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        // Multiple reads must see the same committed ledger, splits, and resolved rates.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var portfolio = await db.Portfolios.AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == portfolioId && p.OwnerAccountId == ownerId, cancellationToken);
        if (portfolio is null) { return null; }
        var trades = await db.InvestmentTransactions.AsNoTracking().Include(t => t.Instrument).Include(t => t.ExchangeRate)
            .Where(t => t.PortfolioId == portfolioId && !t.IsSuperseded && t.ExecutedAtUtc <= asOfUtc).ToListAsync(cancellationToken);
        var ids = trades.Select(t => t.InstrumentId).Distinct().ToArray();
        var splits = await db.StockSplits.AsNoTracking().Where(s => ids.Contains(s.InstrumentId)
            && !s.IsSuperseded && s.EffectiveAtUtc <= asOfUtc).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(portfolio, trades, splits);
    }
}