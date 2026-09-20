using Microsoft.EntityFrameworkCore;

using Npgsql;

using ZiApp.Application.Trading;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence;

public sealed class TradingRepository(ApplicationDbContext db) : ITradingRepository
{
    public async Task<TradingPage<InstrumentDetails>> ListInstrumentsAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Instruments.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            string term = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(item => EF.Functions.ILike(item.Symbol, term, "\\")
                || EF.Functions.ILike(item.Name, term, "\\") || EF.Functions.ILike(item.ExchangeCode, term, "\\"));
        }

        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.Symbol).ThenBy(item => item.ExchangeCode).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(InstrumentDetails.From).ToList(), page, pageSize, count);
    }

    public Task<Instrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Instruments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<bool> AddInstrumentAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        db.Instruments.Add(instrument);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_instruments_symbol_exchange_code" or "ux_instruments_isin",
        })
        {
            db.Entry(instrument).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<TradingPage<TradeDetails>?> ListTradesAsync(Guid ownerId, Guid portfolioId,
        int page, int pageSize, bool includeSuperseded, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(ownerId, portfolioId, cancellationToken))
        {
            return null;
        }

        var query = db.InvestmentTransactions.AsNoTracking().Where(item => item.PortfolioId == portfolioId);
        if (!includeSuperseded)
        {
            query = query.Where(item => !item.IsSuperseded);
        }

        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.ExecutedAtUtc).ThenBy(item => item.FifoOrderId).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(TradeDetails.From).ToList(), page, pageSize, count);
    }

    public async Task<TradeDetails?> FindTradeAsync(Guid ownerId, Guid portfolioId, Guid tradeId, CancellationToken cancellationToken)
    {
        var trade = await db.InvestmentTransactions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == tradeId && item.PortfolioId == portfolioId && item.Portfolio.OwnerAccountId == ownerId,
            cancellationToken);
        return trade is null ? null : TradeDetails.From(trade);
    }

    public async Task<TradingPage<CorrectionDetails>?> ListCorrectionsAsync(Guid ownerId, Guid portfolioId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(ownerId, portfolioId, cancellationToken))
        {
            return null;
        }

        var query = db.TradeCorrections.AsNoTracking().Where(correction => db.InvestmentTransactions
            .Any(trade => trade.Id == correction.OriginalTradeId && trade.PortfolioId == portfolioId));
        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(CorrectionDetails.From).ToList(), page, pageSize, count);
    }

    public async Task<TradingResult<TradeDetails>> MutateAsync(Guid ownerId, Guid portfolioId, Guid instrumentId,
        Func<TradeLedger, TradingResult<TradeMutation>> decide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decide);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Parameterized row lock serializes the entire ledger decision with other writes and archive updates.
        var portfolios = await db.Portfolios.FromSqlInterpolated(
            $"SELECT * FROM portfolios WHERE id = {portfolioId} AND owner_account_id = {ownerId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (portfolios.Count == 0)
        {
            return new(null, TradingError.NotFound);
        }

        var instrument = await FindInstrumentAsync(instrumentId, cancellationToken);
        var trades = await db.InvestmentTransactions.Where(item => item.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        var instrumentIds = trades.Where(item => !item.IsSuperseded).Select(item => item.InstrumentId)
            .Append(instrumentId).Distinct().ToArray();
        var splits = await db.StockSplits.AsNoTracking().Where(item => instrumentIds.Contains(item.InstrumentId))
            .ToListAsync(cancellationToken);
        var decision = decide(new TradeLedger(portfolios[0], instrument, trades, splits));
        if (decision.Value is not { } mutation)
        {
            return new(null, decision.Error);
        }

        try
        {
            if (mutation.Original is not null)
            {
                // Release the active broker-ID reservation before inserting the replacement, within the same transaction.
                mutation.Original.Supersede();
                await db.SaveChangesAsync(cancellationToken);
            }

            db.InvestmentTransactions.Add(mutation.Replacement);
            if (mutation.Correction is not null)
            {
                db.TradeCorrections.Add(mutation.Correction);
            }

            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(mutation.Replacement).ReloadAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(TradeDetails.From(mutation.Replacement));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_investment_transactions_portfolio_broker_id",
        })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(null, TradingError.Duplicate);
        }
    }

    private Task<bool> OwnsAsync(Guid ownerId, Guid portfolioId, CancellationToken cancellationToken) =>
        db.Portfolios.AnyAsync(item => item.Id == portfolioId && item.OwnerAccountId == ownerId, cancellationToken);
}