using System.Text;

using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Application.Reports;

public sealed class ConfiguredTaxService(ICurrentAccount account, IConfiguredTaxRepository repository,
    ITaxReportRepository reports, TimeProvider clock)
{
    public async Task<ConfiguredTaxResult<TaxSettingsDetails>> SaveSettingsAsync(int year, SaveTaxSettingsInput input, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (!ValidYear(year) || !ConfiguredTaxSnapshot.ValidSettings(input)) { return new(null, ConfiguredTaxError.InvalidInput); }
        return new(await repository.SaveSettingsAsync(owner.Value, year, input, ct));
    }

    public async Task<ConfiguredTaxResult<TaxSettingsDetails>> LatestSettingsAsync(int year, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (!ValidYear(year)) { return new(null, ConfiguredTaxError.InvalidInput); }
        var result = await repository.LatestSettingsAsync(owner.Value, year, ct);
        return result is null ? new(null, ConfiguredTaxError.NotFound) : new(result);
    }

    public async Task<ConfiguredTaxResult<TradingPage<TaxSettingsDetails>>> SettingsHistoryAsync(int year, int page, int pageSize, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (!ValidYear(year) || !TradingValidation.IsPageValid(page, pageSize)) { return new(null, ConfiguredTaxError.InvalidInput); }
        return new(await repository.SettingsHistoryAsync(owner.Value, year, page, pageSize, ct));
    }

    public async Task<ConfiguredTaxResult<ConfiguredTaxDocument>> CreateAsync(Guid portfolio, CreateConfiguredTaxReportInput input, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (input.SourceReportId == Guid.Empty || input.SettingsId == Guid.Empty) { return new(null, ConfiguredTaxError.InvalidInput); }
        return await repository.CreateAsync(owner.Value, portfolio, input, ct);
    }

    public async Task<ConfiguredTaxResult<ConfiguredTaxDocument>> GetAsync(Guid portfolio, Guid id, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        var saved = await repository.FindAsync(owner.Value, portfolio, id, ct);
        return saved is null ? new(null, ConfiguredTaxError.NotFound) : ConfiguredTaxSnapshot.Read(saved, owner.Value, portfolio);
    }

    public async Task<ConfiguredTaxResult<TradingPage<ConfiguredTaxListItem>>> ListAsync(Guid portfolio, int? year, int page, int pageSize, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (year is not null && !ValidYear(year.Value) || !TradingValidation.IsPageValid(page, pageSize))
        { return new(null, ConfiguredTaxError.InvalidInput); }
        var result = await repository.ListAsync(owner.Value, portfolio, year, page, pageSize, ct);
        return result is null ? new(null, ConfiguredTaxError.NotFound) : new(result);
    }

    public async Task<ConfiguredTaxResult<ReportDownload>> ExportAsync(Guid portfolio, Guid id, string format, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        if (format is not ("csv" or "json")) { return new(null, ConfiguredTaxError.InvalidInput); }
        var saved = await repository.FindAsync(owner.Value, portfolio, id, ct);
        if (saved is null) { return new(null, ConfiguredTaxError.NotFound); }
        var read = ConfiguredTaxSnapshot.Read(saved, owner.Value, portfolio);
        if (read.Value is null) { return new(null, read.Error); }
        return new(new(format == "json" ? Encoding.UTF8.GetBytes(saved.SnapshotJson) : ConfiguredTaxSnapshot.Csv(read.Value),
            format == "json" ? "application/json" : "text/csv; charset=utf-8",
            $"ziapp-configured-tax-{saved.TaxYear}-{saved.Id:N}.{format}"));
    }

    public async Task<ConfiguredTaxResult<ConfiguredTaxCurrentStatus>> CurrentStatusAsync(Guid portfolio, Guid id, CancellationToken ct)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(ct);
        if (owner is null) { return new(null, ConfiguredTaxError.AccountUnavailable); }
        var saved = await repository.FindAsync(owner.Value, portfolio, id, ct);
        if (saved is null) { return new(null, ConfiguredTaxError.NotFound); }
        var read = ConfiguredTaxSnapshot.Read(saved, owner.Value, portfolio);
        if (read.Value is null) { return new(null, read.Error); }
        var latest = await repository.LatestSettingsAsync(owner.Value, saved.TaxYear, ct);
        var source = await reports.CurrentStatusAsync(owner.Value, portfolio, saved.SourceReportId, ct);
        return new(new(id, latest?.Id, latest?.Id == saved.SettingsId,
            source.Value?.Status ?? "SourceUnavailable", source.Value?.MatchesCurrentInputs, false));
    }

    private bool ValidYear(int year) => year >= 2000 && year <= Math.Min(clock.GetUtcNow().Year, 9998);
}
