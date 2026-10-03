using System.Data;

using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Reports;
using ZiApp.Application.Trading;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Infrastructure.Persistence;

public sealed class AnnualSummaryRepository(ApplicationDbContext db, ITaxReportRepository reports, TimeProvider clock) : IAnnualSummaryRepository
{
    public async Task<AnnualResult<AnnualSummaryDocument>> CreateAsync(Guid ownerId, CreateAnnualSummaryInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            Guid[] ids = input.Reports.Select(r => r.ReportId).ToArray();
            var owned = db.TaxCalculationRuns.AsNoTracking()
                .Where(r => ids.Contains(r.Id) && r.Portfolio.OwnerAccountId == ownerId);
            // Bound materialization before loading potentially large source snapshots.
            var sizes = await owned.Select(r => new { r.Id, r.PortfolioId, Length = r.SnapshotJson == null ? 0 : r.SnapshotJson.Length })
                .ToListAsync(cancellationToken);
            if (sizes.Count != ids.Length || input.Reports.Any(s => !sizes.Any(r => r.Id == s.ReportId && r.PortfolioId == s.PortfolioId)))
            { return new(null, AnnualSummaryError.NotFound); }
            if (sizes.Sum(r => (long)r.Length) > AnnualSummarySnapshot.MaxBytes)
            { return new(null, AnnualSummaryError.TooLarge); }
            var runs = await owned.ToListAsync(cancellationToken);
            var inventory = await InventoryAsync(ownerId, cancellationToken);
            DateTimeOffset now = clock.GetUtcNow();
            var captured = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            var built = AnnualSummaryBuilder.Build(ownerId, input, inventory, runs, captured);
            db.AnnualPreparationDrafts.Add(built.Draft);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(built.Document);
        }
        catch (AnnualSummaryValidationException error) { return new(null, error.Error); }
    }

    public async Task<TradingPage<AnnualSummaryListItem>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.AnnualPreparationDrafts.AsNoTracking().Where(r => r.OwnerAccountId == ownerId);
        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new AnnualSummaryListItem(r.Id, r.TaxYear, r.CreatedAtUtc, "Draft", false, r.SnapshotSha256))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }

    public Task<AnnualPreparationDraft?> FindAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        db.AnnualPreparationDrafts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.OwnerAccountId == ownerId, cancellationToken);

    public async Task<AnnualResult<AnnualSummaryCurrentStatus>> CurrentStatusAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await FindAsync(ownerId, id, cancellationToken);
        if (draft is null) { return new(null, AnnualSummaryError.NotFound); }
        var read = AnnualSummarySnapshot.Read(draft);
        if (read.Value is null) { return new(null, read.Error); }
        List<AnnualPortfolio> inventory;
        try { inventory = await InventoryAsync(ownerId, cancellationToken); }
        catch (AnnualSummaryValidationException error) { return new(null, error.Error); }
        bool inventoryChanged = !inventory.SequenceEqual(read.Value.PortfolioInventory);
        var sources = new List<AnnualSourceStatus>();
        foreach (var source in read.Value.SourceReports)
        {
            var report = source.Report;
            var current = await reports.FindAsync(ownerId, report.PortfolioId, report.Id, cancellationToken);
            if (current is null || current.SnapshotSha256 != source.SnapshotSha256)
            {
                sources.Add(new(report.PortfolioId, report.Id, "SourceUnavailableOrChanged", null, false));
                continue;
            }
            var status = await reports.CurrentStatusAsync(ownerId, report.PortfolioId, report.Id, cancellationToken);
            sources.Add(status.Value is { } value
                ? new(report.PortfolioId, report.Id, value.Status, value.MatchesCurrentInputs, value.UsesCurrentCalculationVersion)
                : new(report.PortfolioId, report.Id, "SourceInvalid", null, false));
        }
        bool unchanged = !inventoryChanged && sources.All(s => s.MatchesCurrentInputs == true && s.UsesCurrentCalculationVersion);
        return new(new(id, inventoryChanged, inventory, sources, unchanged ? "Current" : "ReviewRequired", false));
    }

    private async Task<List<AnnualPortfolio>> InventoryAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var inventory = await db.Portfolios.AsNoTracking().Where(p => p.OwnerAccountId == ownerId)
            .OrderBy(p => p.Id).Take(AnnualSummarySnapshot.MaxPortfolios + 1)
            .Select(p => new AnnualPortfolio(p.Id, p.Name, p.IsArchived)).ToListAsync(cancellationToken);
        if (inventory.Count > AnnualSummarySnapshot.MaxPortfolios)
        { throw new AnnualSummaryValidationException(AnnualSummaryError.TooLarge); }
        return inventory;
    }
}
