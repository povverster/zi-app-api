using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public enum AnnualSummaryError
{
    AccountUnavailable, NotFound, InvalidInput, InvalidSource, LegacySnapshotUnavailable,
    IncompatiblePolicies, KnownOverlap, TooLarge, ArithmeticNotRepresentable, InvalidSnapshot
}

public sealed record AnnualResult<T>(T? Value, AnnualSummaryError? Error = null) where T : class;
public sealed record AnnualReportSelection(Guid PortfolioId, Guid ReportId, string? BrokerAccountReference);
public sealed record ExternalInvestmentInput(string Reference, string Description, string Coverage,
    string ProfitUah, string EvidenceReference);
public sealed record PriorLossClaim(int OriginYear, string AmountUah, string PreviouslyUsedUah, string EvidenceReference);

// Coverage states are case-sensitive: Unknown, None, Provided. Null is never a financial zero.
public sealed record CreateAnnualSummaryInput(int TaxYear, IReadOnlyList<AnnualReportSelection> Reports,
    string ExternalCoverage, IReadOnlyList<ExternalInvestmentInput> ExternalInvestments,
    string PriorLossCoverage, IReadOnlyList<PriorLossClaim> PriorLossClaims,
    bool PortfolioCoverageConfirmed, bool NoOverlapConfirmed);

public sealed record AnnualPortfolio(Guid Id, string Name, bool IsArchived);
public sealed record AnnualSourceReport(string SnapshotSha256, TaxReportDocument Report);
public sealed record AnnualSummaryDocument(Guid Id, Guid OwnerAccountId, Guid CreatedByAccountId, int TaxYear,
    DateTimeOffset CreatedAtUtc, string SchemaVersion, string Status, bool IsTaxReady,
    CreateAnnualSummaryInput Inputs, IReadOnlyList<AnnualPortfolio> PortfolioInventory,
    IReadOnlyList<Guid> OmittedPortfolioIds, IReadOnlyList<AnnualSourceReport> SourceReports,
    GainTotals SelectedReportsSubtotal, string? ExternalProfitSubtotalUah, IReadOnlyList<string> ReviewIssues);
public sealed record AnnualSummaryListItem(Guid Id, int TaxYear, DateTimeOffset CreatedAtUtc,
    string Status, bool IsTaxReady, string SnapshotSha256);
public sealed record AnnualSourceStatus(Guid PortfolioId, Guid ReportId, string Status,
    bool? MatchesCurrentInputs, bool UsesCurrentCalculationVersion);
public sealed record AnnualSummaryCurrentStatus(Guid Id, bool PortfolioInventoryChanged,
    IReadOnlyList<AnnualPortfolio> CurrentPortfolioInventory, IReadOnlyList<AnnualSourceStatus> Sources,
    string Status, bool IsTaxReady);
public sealed record BuiltAnnualSummary(AnnualPreparationDraft Draft, AnnualSummaryDocument Document);

public interface IAnnualSummaryRepository
{
    Task<AnnualResult<AnnualSummaryDocument>> CreateAsync(Guid ownerId, CreateAnnualSummaryInput input, CancellationToken cancellationToken);
    Task<TradingPage<AnnualSummaryListItem>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AnnualPreparationDraft?> FindAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<AnnualResult<AnnualSummaryCurrentStatus>> CurrentStatusAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

public sealed class AnnualSummaryValidationException(AnnualSummaryError error) : Exception(error.ToString())
{
    public AnnualSummaryError Error { get; } = error;
}
