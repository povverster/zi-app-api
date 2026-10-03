using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Reports;

public enum ReportError { AccountUnavailable, NotFound, InvalidInput, UnsupportedCurrency, InvalidLedger, UnresolvedRates, TooLarge, LegacySnapshotUnavailable, InvalidSnapshot }
public sealed record ReportResult<T>(T? Value, ReportError? Error = null, IReadOnlyList<RateBlocker>? RateBlockers = null) where T : class;
public sealed record CreateReportInput(int TaxYear);
public sealed record ReportSummary(Guid Id, Guid PortfolioId, int TaxYear, string CalculationVersion,
    DateTimeOffset CreatedAtUtc, string Status, bool IsTaxReady, string? InputSha256);
public sealed record ReportCurrentStatus(Guid ReportId, string Status, bool? MatchesCurrentInputs,
    string SavedInputSha256, string? CurrentInputSha256, bool UsesCurrentCalculationVersion);
public sealed record ReportPortfolio(Guid Id, string Name, string BaseCurrencyCode);
public sealed record ReportTrade(Guid Id, Guid InstrumentId, Guid FifoOrderId, string Side, DateTimeOffset ExecutedAtUtc,
    string? ExecutedAtOriginal, DateOnly BrokerDate, string Quantity, string UnitPriceUsd, string FeeUsd,
    string? BrokerTransactionId, Guid? ExchangeRateId, DateOnly? ExchangeRateSelectedDate,
    string? ExchangeRatePolicy, Guid? ExchangeRateResolvedByAccountId, DateTimeOffset? ExchangeRateResolvedAtUtc);
public sealed record ReportRate(Guid Id, string CurrencyCode, DateOnly EffectiveDate, string RateToUah,
    string Source, DateTimeOffset RetrievedAtUtc, DateOnly? CalculationDate, string? SourceUrl,
    string? ResponseSha256, string? RawResponseJson);
public sealed record ReportInputs(ReportPortfolio Portfolio, IReadOnlyList<InstrumentDetails> Instruments,
    IReadOnlyList<ReportTrade> Trades, IReadOnlyList<SplitDetails> Splits, IReadOnlyList<ReportRate> Rates);
public sealed record ReportMatch(Guid InstrumentId, MatchDetails Values, GainTotals Totals);
public sealed record TaxReportDocument(Guid Id, Guid PortfolioId, int TaxYear, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReplayThroughUtc, string Status, bool IsTaxReady, string SchemaVersion,
    string CalculationVersion, string YearPolicyVersion, string RoundingPolicyVersion,
    string InputSha256, ReportInputs Inputs, IReadOnlyList<ReportMatch> Matches, GainTotals Totals);
public sealed record PreparedReport(ReportInputs Inputs, IReadOnlyList<InvestmentTransaction> Trades);
public sealed record BuiltReport(TaxCalculationRun Run, IReadOnlyList<TaxLotMatchSnapshot> Matches, TaxReportDocument Document);
public sealed record ReportDownload(byte[] Content, string ContentType, string FileName);

public interface ITaxReportRepository
{
    Task<ReportResult<TaxReportDocument>> CreateAsync(Guid ownerId, Guid portfolioId, int year, CancellationToken cancellationToken);
    Task<TradingPage<ReportSummary>?> ListAsync(Guid ownerId, Guid portfolioId, int? year, int page, int pageSize, CancellationToken cancellationToken);
    Task<TaxCalculationRun?> FindAsync(Guid ownerId, Guid portfolioId, Guid id, CancellationToken cancellationToken);
    Task<ReportResult<ReportCurrentStatus>> CurrentStatusAsync(Guid ownerId, Guid portfolioId, Guid id, CancellationToken cancellationToken);
}

public sealed class ReportValidationException(ReportError error, IReadOnlyList<RateBlocker>? blockers = null) : Exception(error.ToString())
{
    public ReportError Error { get; } = error;
    public IReadOnlyList<RateBlocker>? Blockers { get; } = blockers;
}
