using System.Data;
using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Reports;
using ZiApp.Application.Trading;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Infrastructure.Persistence;

public sealed class ConfiguredTaxRepository(ApplicationDbContext db, TimeProvider clock) : IConfiguredTaxRepository
{
    public async Task<TaxSettingsDetails> SaveSettingsAsync(Guid owner, int year, SaveTaxSettingsInput input, CancellationToken cancellationToken)
    {
        var revision = new TaxSettingsRevision(Guid.CreateVersion7(), owner, year,
            decimal.Parse(input.InvestmentIncomePercent, CultureInfo.InvariantCulture),
            decimal.Parse(input.MilitaryPercent, CultureInfo.InvariantCulture),
            decimal.Parse(input.DividendIncomePercent, CultureInfo.InvariantCulture), CapturedNow());
        db.TaxSettingsRevisions.Add(revision);
        await db.SaveChangesAsync(cancellationToken);
        return TaxSettingsDetails.From(revision);
    }

    public async Task<TaxSettingsDetails?> LatestSettingsAsync(Guid owner, int year, CancellationToken cancellationToken)
    {
        var revision = await SettingsQuery(owner, year).FirstOrDefaultAsync(cancellationToken);
        return revision is null ? null : TaxSettingsDetails.From(revision);
    }

    public async Task<TradingPage<TaxSettingsDetails>> SettingsHistoryAsync(Guid owner, int year, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = SettingsQuery(owner, year);
        int count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(TaxSettingsDetails.From).ToList(), page, pageSize, count);
    }

    public async Task<ConfiguredTaxResult<ConfiguredTaxDocument>> CreateAsync(Guid owner, Guid portfolio,
        CreateConfiguredTaxReportInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var sourceQuery = db.TaxCalculationRuns.AsNoTracking().Where(r => r.Id == input.SourceReportId
            && r.PortfolioId == portfolio && r.Portfolio.OwnerAccountId == owner);
        var size = await sourceQuery.Select(r => new { Length = r.SnapshotJson == null ? 0 : r.SnapshotJson.Length }).SingleOrDefaultAsync(cancellationToken);
        var settings = await db.TaxSettingsRevisions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == input.SettingsId && s.OwnerAccountId == owner, cancellationToken);
        if (size is null || settings is null) { return new(null, ConfiguredTaxError.NotFound); }
        if (size.Length > ReportSnapshot.MaxBytes) { return new(null, ConfiguredTaxError.TooLarge); }
        var source = await sourceQuery.SingleAsync(cancellationToken);
        if (source.SnapshotJson is not null && Encoding.UTF8.GetByteCount(source.SnapshotJson) > ReportSnapshot.MaxBytes)
        { return new(null, ConfiguredTaxError.TooLarge); }
        var built = ConfiguredTaxSnapshot.Build(owner, source, settings, CapturedNow());
        if (built.Value is null) { return built; }
        string json = ReportSnapshot.Serialize(built.Value);
        if (Encoding.UTF8.GetByteCount(json) > ConfiguredTaxSnapshot.MaxBytes) { return new(null, ConfiguredTaxError.TooLarge); }
        db.ConfiguredTaxReports.Add(new(built.Value.Id, source.Id, settings.Id, built.Value.TaxYear, built.Value.CreatedAtUtc,
            ConfiguredTaxSnapshot.SchemaVersion, json, ReportSnapshot.Hash(json)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return built;
    }

    public Task<ConfiguredTaxReport?> FindAsync(Guid owner, Guid portfolio, Guid id, CancellationToken cancellationToken) =>
        db.ConfiguredTaxReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.SourceReport.PortfolioId == portfolio
            && r.SourceReport.Portfolio.OwnerAccountId == owner && r.Settings.OwnerAccountId == owner, cancellationToken);

    public async Task<TradingPage<ConfiguredTaxListItem>?> ListAsync(Guid owner, Guid portfolio, int? year,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolio && p.OwnerAccountId == owner, cancellationToken)) { return null; }
        var query = db.ConfiguredTaxReports.AsNoTracking().Where(r => r.SourceReport.PortfolioId == portfolio
            && r.SourceReport.Portfolio.OwnerAccountId == owner && r.Settings.OwnerAccountId == owner
            && (year == null || r.TaxYear == year));
        int count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ConfiguredTaxListItem(r.Id, r.SourceReportId, r.SettingsId, r.TaxYear, r.CreatedAtUtc, "UserConfigured", false))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }

    private IOrderedQueryable<TaxSettingsRevision> SettingsQuery(Guid owner, int year) =>
        db.TaxSettingsRevisions.AsNoTracking().Where(s => s.OwnerAccountId == owner && s.TaxYear == year)
            .OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id);

    private DateTimeOffset CapturedNow()
    {
        DateTimeOffset now = clock.GetUtcNow();
        return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }
}
