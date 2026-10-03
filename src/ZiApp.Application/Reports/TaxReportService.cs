using System.Text;

using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Application.Reports;

public sealed class TaxReportService(ICurrentAccount account, ITaxReportRepository repository, TimeProvider clock)
{
    public async Task<ReportResult<TaxReportDocument>> CreateAsync(Guid portfolioId, CreateReportInput input, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, ReportError.AccountUnavailable); }
        if (!ValidYear(input.TaxYear)) { return new(null, ReportError.InvalidInput); }
        return await repository.CreateAsync(owner.Value, portfolioId, input.TaxYear, cancellationToken);
    }

    public async Task<ReportResult<TradingPage<ReportSummary>>> ListAsync(Guid portfolioId, int? year, int page, int pageSize, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, ReportError.AccountUnavailable); }
        if (year is not null && !ValidYear(year.Value) || !TradingValidation.IsPageValid(page, pageSize))
        { return new(null, ReportError.InvalidInput); }
        var result = await repository.ListAsync(owner.Value, portfolioId, year, page, pageSize, cancellationToken);
        return result is null ? new(null, ReportError.NotFound) : new(result);
    }

    public async Task<ReportResult<TaxReportDocument>> GetAsync(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, ReportError.AccountUnavailable); }
        var run = await repository.FindAsync(owner.Value, portfolioId, id, cancellationToken);
        return run is null ? new(null, ReportError.NotFound) : ReportSnapshot.Read(run);
    }

    public async Task<ReportResult<ReportCurrentStatus>> CurrentStatusAsync(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        return owner is null ? new(null, ReportError.AccountUnavailable)
            : await repository.CurrentStatusAsync(owner.Value, portfolioId, id, cancellationToken);
    }

    public async Task<ReportResult<ReportDownload>> ExportAsync(Guid portfolioId, Guid id, string format, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, ReportError.AccountUnavailable); }
        if (format is not ("csv" or "json")) { return new(null, ReportError.InvalidInput); }
        var run = await repository.FindAsync(owner.Value, portfolioId, id, cancellationToken);
        if (run is null) { return new(null, ReportError.NotFound); }
        var saved = ReportSnapshot.Read(run);
        if (saved.Value is null) { return new(null, saved.Error); }
        return new(new(format == "json" ? Encoding.UTF8.GetBytes(run.SnapshotJson!) : TaxReportCsv.Export(saved.Value),
            format == "json" ? "application/json" : "text/csv; charset=utf-8",
            $"ziapp-draft-{run.TaxYear}-{run.Id:N}.{format}"));
    }

    private bool ValidYear(int year) => year >= 2000 && year <= clock.GetUtcNow().Year;
}
