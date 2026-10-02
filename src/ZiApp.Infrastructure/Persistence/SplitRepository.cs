using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence;

public sealed class SplitRepository(ApplicationDbContext db, TimeProvider clock) : ISplitRepository
{
    public async Task<TradingPage<SplitDetails>?> ListAsync(Guid instrumentId, int page, int pageSize, bool includeSuperseded, CancellationToken cancellationToken)
    {
        if (!await db.Instruments.AnyAsync(i => i.Id == instrumentId, cancellationToken)) { return null; }
        var query = db.StockSplits.AsNoTracking().Where(s => s.InstrumentId == instrumentId && (includeSuperseded || !s.IsSuperseded));
        int count = await query.CountAsync(cancellationToken);
        var splits = await query.OrderBy(s => s.EffectiveAtUtc).ThenBy(s => s.FifoOrderId).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(splits.Select(SplitDetails.From).ToList(), page, pageSize, count);
    }

    public async Task<SplitDetails?> FindAsync(Guid instrumentId, Guid id, CancellationToken cancellationToken)
    {
        var split = await db.StockSplits.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.InstrumentId == instrumentId, cancellationToken);
        return split is null ? null : SplitDetails.From(split);
    }

    public async Task<LedgerResult<SplitDetails>> RecordAsync(Guid actorId, Guid instrumentId, Guid? originalId,
        DateTimeOffset effectiveAt, SplitInput input, decimal numerator, decimal denominator, string? reason, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LedgerWriteLock.ExclusiveAsync(db, cancellationToken);
        var instrument = await db.Instruments.AsNoTracking().SingleOrDefaultAsync(i => i.Id == instrumentId, cancellationToken);
        if (instrument is null) { return new(null, LedgerError.NotFound); }
        if (instrument.CurrencyCode != "USD") { return new(null, LedgerError.UnsupportedCurrency); }
        var splits = await db.StockSplits.Where(s => s.InstrumentId == instrumentId).ToListAsync(cancellationToken);
        var original = originalId is null ? null : splits.SingleOrDefault(s => s.Id == originalId);
        if (originalId is not null && original is null) { return new(null, LedgerError.NotFound); }
        if (original?.IsSuperseded == true) { return new(null, LedgerError.AlreadyCorrected); }
        var active = splits.Where(s => !s.IsSuperseded && s.Id != originalId).ToList();
        if (active.Any(s => s.EffectiveAtUtc == effectiveAt)) { return new(null, LedgerError.Duplicate); }
        var replacement = new StockSplit(Guid.CreateVersion7(), instrumentId, effectiveAt, numerator, denominator,
            original?.FifoOrderId, input.EffectiveAt, actorId, clock.GetUtcNow(), input.SourceReference.Trim(), originalId, reason?.Trim());
        active.Add(replacement);

        // Global corporate actions affect every account, including archived portfolios.
        // The advisory lock also blocks new first trades while these affected rows are found.
        await db.Portfolios.FromSqlInterpolated($"""
            SELECT p.* FROM portfolios p
            WHERE EXISTS (SELECT 1 FROM investment_transactions t WHERE t.portfolio_id = p.id AND t.instrument_id = {instrumentId})
            ORDER BY p.id FOR UPDATE
            """).ToListAsync(cancellationToken);
        var trades = await db.InvestmentTransactions.AsNoTracking().Where(t => t.InstrumentId == instrumentId && !t.IsSuperseded)
            .ToListAsync(cancellationToken);
        try
        {
            foreach (var portfolio in trades.GroupBy(t => t.PortfolioId))
            {
                if (!TradeQuantityValidator.CanExecute(portfolio, active)) { return new(null, LedgerError.InvalidLedger); }
            }
            // Validate the ratio even when no portfolio holds the instrument yet.
            if (numerator / denominator == 0m) { return new(null, LedgerError.InvalidInput); }
        }
        catch (OverflowException) { return new(null, LedgerError.InvalidInput); }

        if (original is not null)
        {
            original.Supersede();
            // Release the active ordering-key reservation before inserting its replacement.
            await db.SaveChangesAsync(cancellationToken);
        }
        db.StockSplits.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(replacement).ReloadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(SplitDetails.From(replacement));
    }
}