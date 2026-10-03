using System.Data;

using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Ledger;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Infrastructure.Persistence;

public sealed class TaxReportRepository(ApplicationDbContext db, TimeProvider clock) : ITaxReportRepository
{
    public async Task<ReportResult<TaxReportDocument>> CreateAsync(Guid ownerId, Guid portfolioId, int year, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            DateTimeOffset now = clock.GetUtcNow();
            // PostgreSQL timestamps retain microseconds. Store exactly the same instant in JSON.
            var captured = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            var ledger = await LoadAsync(ownerId, portfolioId, captured, cancellationToken);
            if (ledger is null) { return new(null, ReportError.NotFound); }
            BuiltReport built;
            try { built = TaxReportBuilder.Build(ledger, year, captured); }
            catch (Exception error) when (error is OverflowException or ArgumentException or InvalidOperationException)
            { return new(null, ReportError.InvalidLedger); }
            db.TaxCalculationRuns.Add(built.Run);
            db.TaxLotMatchSnapshots.AddRange(built.Matches);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(built.Document);
        }
        catch (ReportValidationException error) { return new(null, error.Error, error.Blockers); }
    }

    public async Task<TradingPage<ReportSummary>?> ListAsync(Guid ownerId, Guid portfolioId, int? year, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId && p.OwnerAccountId == ownerId, cancellationToken)) { return null; }
        var query = Owned(ownerId, portfolioId).Where(r => year == null || r.TaxYear == year);
        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReportSummary(r.Id, r.PortfolioId, r.TaxYear, r.CalculationVersion, r.CreatedAtUtc,
                r.ReportSchemaVersion == null ? "LegacySnapshotUnavailable" : "Draft", false, r.InputSha256))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }

    public Task<TaxCalculationRun?> FindAsync(Guid ownerId, Guid portfolioId, Guid id, CancellationToken cancellationToken) =>
        Owned(ownerId, portfolioId).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<ReportResult<ReportCurrentStatus>> CurrentStatusAsync(Guid ownerId, Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var run = await FindAsync(ownerId, portfolioId, id, cancellationToken);
        if (run is null) { return new(null, ReportError.NotFound); }
        var saved = ReportSnapshot.Read(run);
        if (saved.Value is null) { return new(null, saved.Error); }
        bool currentVersion = run.CalculationVersion == FifoRealizedGainCalculator.HoldingsVersion;
        try
        {
            var ledger = await LoadAsync(ownerId, portfolioId, clock.GetUtcNow(), cancellationToken);
            if (ledger is null) { return new(null, ReportError.NotFound); }
            string currentHash = ReportSnapshot.InputHash(TaxReportBuilder.Prepare(ledger, run.TaxYear).Inputs);
            bool matches = currentHash == run.InputSha256;
            return new(new(run.Id, matches ? "Current" : "InputsChanged", matches, run.InputSha256!, currentHash, currentVersion));
        }
        catch (Exception error) when (error is ReportValidationException or OverflowException or ArgumentException or InvalidOperationException)
        {
            return new(new(run.Id, "CurrentInputsInvalid", null, run.InputSha256!, null, currentVersion));
        }
    }

    private IQueryable<TaxCalculationRun> Owned(Guid ownerId, Guid portfolioId) => db.TaxCalculationRuns.AsNoTracking()
        .Where(r => r.PortfolioId == portfolioId && r.Portfolio.OwnerAccountId == ownerId);

    private async Task<HoldingsLedger?> LoadAsync(Guid ownerId, Guid portfolioId, DateTimeOffset through, CancellationToken cancellationToken)
    {
        var portfolio = await db.Portfolios.AsNoTracking().SingleOrDefaultAsync(p => p.Id == portfolioId && p.OwnerAccountId == ownerId, cancellationToken);
        if (portfolio is null) { return null; }
        var trades = await db.InvestmentTransactions.AsNoTracking().Include(t => t.Instrument).Include(t => t.ExchangeRate)
            .Where(t => t.PortfolioId == portfolioId && !t.IsSuperseded && t.ExecutedAtUtc <= through)
            .OrderBy(t => t.Id).Take(ReportSnapshot.MaxTrades + 1).ToListAsync(cancellationToken);
        if (trades.Count > ReportSnapshot.MaxTrades) { throw new ReportValidationException(ReportError.TooLarge); }
        var ids = trades.Select(t => t.InstrumentId).Distinct().ToArray();
        var splits = await db.StockSplits.AsNoTracking().Where(s => ids.Contains(s.InstrumentId) && !s.IsSuperseded && s.EffectiveAtUtc <= through)
            .OrderBy(s => s.Id).Take(ReportSnapshot.MaxSplits + 1).ToListAsync(cancellationToken);
        if (splits.Count > ReportSnapshot.MaxSplits) { throw new ReportValidationException(ReportError.TooLarge); }
        return new(portfolio, trades, splits);
    }
}
