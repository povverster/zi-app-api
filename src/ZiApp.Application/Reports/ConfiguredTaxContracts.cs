using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public enum ConfiguredTaxError { AccountUnavailable, NotFound, InvalidInput, YearMismatch, InvalidSource, LegacySnapshotUnavailable, TooLarge, ArithmeticNotRepresentable, InvalidSnapshot }
public sealed record ConfiguredTaxResult<T>(T? Value, ConfiguredTaxError? Error = null) where T : class;
public sealed record SaveTaxSettingsInput(string InvestmentIncomePercent, string MilitaryPercent, string DividendIncomePercent);
public sealed record TaxSettingsDetails(Guid Id, int TaxYear, string InvestmentIncomePercent, string MilitaryPercent,
    string DividendIncomePercent, DateTimeOffset CreatedAtUtc)
{
    public static TaxSettingsDetails From(TaxSettingsRevision value) => new(value.Id, value.TaxYear,
        LedgerDecimal.Format(value.InvestmentIncomePercent), LedgerDecimal.Format(value.MilitaryPercent),
        LedgerDecimal.Format(value.DividendIncomePercent), value.CreatedAtUtc);
}
public sealed record CreateConfiguredTaxReportInput(Guid SourceReportId, Guid SettingsId);
public sealed record ConfiguredTaxDocument(Guid Id, Guid PortfolioId, int TaxYear, Guid CreatedByAccountId,
    DateTimeOffset CreatedAtUtc, string Status, bool IsTaxReady, string SchemaVersion, string CalculationVersion,
    string RoundingPolicyVersion, string IncomeScope, string DividendStatus, TaxSettingsDetails Settings,
    string SourceSnapshotSha256, TaxReportDocument SourceReport, ConfiguredTaxAmounts Amounts);
public sealed record ConfiguredTaxListItem(Guid Id, Guid SourceReportId, Guid SettingsId, int TaxYear,
    DateTimeOffset CreatedAtUtc, string Status, bool IsTaxReady);
public sealed record ConfiguredTaxCurrentStatus(Guid Id, Guid? LatestSettingsId, bool UsesLatestSettings,
    string SourceStatus, bool? MatchesCurrentSourceInputs, bool IsTaxReady);

public interface IConfiguredTaxRepository
{
    Task<TaxSettingsDetails> SaveSettingsAsync(Guid owner, int year, SaveTaxSettingsInput input, CancellationToken cancellationToken);
    Task<TaxSettingsDetails?> LatestSettingsAsync(Guid owner, int year, CancellationToken cancellationToken);
    Task<TradingPage<TaxSettingsDetails>> SettingsHistoryAsync(Guid owner, int year, int page, int pageSize, CancellationToken cancellationToken);
    Task<ConfiguredTaxResult<ConfiguredTaxDocument>> CreateAsync(Guid owner, Guid portfolio, CreateConfiguredTaxReportInput input, CancellationToken cancellationToken);
    Task<ConfiguredTaxReport?> FindAsync(Guid owner, Guid portfolio, Guid id, CancellationToken cancellationToken);
    Task<TradingPage<ConfiguredTaxListItem>?> ListAsync(Guid owner, Guid portfolio, int? year, int page, int pageSize, CancellationToken cancellationToken);
}
