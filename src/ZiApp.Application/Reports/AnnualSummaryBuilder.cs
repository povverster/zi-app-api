using System.Globalization;
using System.Text;

using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public static class AnnualSummaryBuilder
{
    public static bool ValidInput(CreateAnnualSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.TaxYear != 2025 || !input.PortfolioCoverageConfirmed || !input.NoOverlapConfirmed
            || input.Reports is null || input.ExternalInvestments is null || input.PriorLossClaims is null
            || input.Reports.Count > AnnualSummarySnapshot.MaxReports || input.ExternalInvestments.Count > 100 || input.PriorLossClaims.Count > 50
            || !CoverageValid(input.ExternalCoverage, input.ExternalInvestments.Count)
            || !CoverageValid(input.PriorLossCoverage, input.PriorLossClaims.Count)
            || input.Reports.Any(r => r is null || r.PortfolioId == Guid.Empty || r.ReportId == Guid.Empty
                || r.BrokerAccountReference is not null && !Text(r.BrokerAccountReference, 200))
            || input.Reports.Select(r => r.PortfolioId).Distinct().Count() != input.Reports.Count
            || input.Reports.Select(r => r.ReportId).Distinct().Count() != input.Reports.Count)
        { return false; }
        if (input.ExternalInvestments.Any(e => e is null || !Text(e.Reference, 200) || !Text(e.Description, 500)
            || !Text(e.Coverage, 500) || !Text(e.EvidenceReference, 1000) || !AnnualAmounts.IsInput(e.ProfitUah, true))
            || input.ExternalInvestments.Select(e => e.Reference.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != input.ExternalInvestments.Count)
        { return false; }
        return !input.PriorLossClaims.Any(c => c is null || c.OriginYear is < 2000 or >= 2025
            || !AnnualAmounts.IsInput(c.AmountUah) || !AnnualAmounts.IsInput(c.PreviouslyUsedUah)
            || !Text(c.EvidenceReference, 1000)
            || decimal.Parse(c.PreviouslyUsedUah, CultureInfo.InvariantCulture) > decimal.Parse(c.AmountUah, CultureInfo.InvariantCulture));
    }

    public static BuiltAnnualSummary Build(Guid ownerId, CreateAnnualSummaryInput input,
        IReadOnlyList<AnnualPortfolio> inventory, IReadOnlyList<TaxCalculationRun> runs, DateTimeOffset captured)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(runs);
        if (!ValidInput(input)) { throw new AnnualSummaryValidationException(AnnualSummaryError.InvalidInput); }
        if (inventory.Count > AnnualSummarySnapshot.MaxPortfolios)
        { throw new AnnualSummaryValidationException(AnnualSummaryError.TooLarge); }
        var reports = new List<AnnualSourceReport>();
        var issues = new HashSet<string>(StringComparer.Ordinal) { "LegalReviewPending", "ExternalOverlapNotAutomaticallyVerifiable" };
        var tradeIds = new HashSet<Guid>();
        var fifoIds = new HashSet<Guid>();
        var brokerIds = new HashSet<(string Account, string Id)>();
        var fingerprints = new Dictionary<string, Guid>(StringComparer.Ordinal);
        long sourceBytes = 0;
        foreach (var selection in input.Reports)
        {
            var run = runs.SingleOrDefault(r => r.Id == selection.ReportId && r.PortfolioId == selection.PortfolioId);
            if (run is null || !inventory.Any(p => p.Id == selection.PortfolioId))
            { throw new AnnualSummaryValidationException(AnnualSummaryError.NotFound); }
            if (run.TaxYear != input.TaxYear) { throw new AnnualSummaryValidationException(AnnualSummaryError.InvalidInput); }
            sourceBytes += run.SnapshotJson is null ? 0 : Encoding.UTF8.GetByteCount(run.SnapshotJson);
            if (sourceBytes > AnnualSummarySnapshot.MaxBytes)
            { throw new AnnualSummaryValidationException(AnnualSummaryError.TooLarge); }
            var read = ReportSnapshot.Read(run);
            if (read.Value is null)
            {
                throw new AnnualSummaryValidationException(read.Error == ReportError.LegacySnapshotUnavailable
                    ? AnnualSummaryError.LegacySnapshotUnavailable : AnnualSummaryError.InvalidSource);
            }
            var report = read.Value;
            if (report.Totals is null || report.Matches is null || report.Inputs.Trades is null
                || report.Inputs.Portfolio is null || report.Inputs.Portfolio.Id != selection.PortfolioId
                || report.Inputs.Portfolio.BaseCurrencyCode != "USD"
                || string.IsNullOrWhiteSpace(report.CalculationVersion) || string.IsNullOrWhiteSpace(report.YearPolicyVersion)
                || string.IsNullOrWhiteSpace(report.RoundingPolicyVersion))
            { throw new AnnualSummaryValidationException(AnnualSummaryError.InvalidSource); }
            if (reports.Count > 0 && !SamePolicies(reports[0].Report, report))
            { throw new AnnualSummaryValidationException(AnnualSummaryError.IncompatiblePolicies); }
            string? broker = selection.BrokerAccountReference?.Trim().ToUpperInvariant();
            if (broker is null) { issues.Add("BrokerAccountCoverageUnknown"); }
            foreach (var trade in report.Inputs.Trades)
            {
                if (trade is null || trade.Id == Guid.Empty || trade.FifoOrderId == Guid.Empty)
                { throw new AnnualSummaryValidationException(AnnualSummaryError.InvalidSource); }
                if (!tradeIds.Add(trade.Id) || !fifoIds.Add(trade.FifoOrderId))
                { throw new AnnualSummaryValidationException(AnnualSummaryError.KnownOverlap); }
                if (broker is not null && trade.BrokerTransactionId is not null
                    && !brokerIds.Add((broker, trade.BrokerTransactionId.Trim())))
                { throw new AnnualSummaryValidationException(AnnualSummaryError.KnownOverlap); }
                string fingerprint = ReportSnapshot.Serialize(new { trade.InstrumentId, trade.Side, trade.ExecutedAtUtc,
                    trade.Quantity, trade.UnitPriceUsd, trade.FeeUsd });
                if (fingerprints.TryGetValue(fingerprint, out Guid previous) && previous != selection.PortfolioId)
                { issues.Add("PossibleDuplicateTrades"); }
                fingerprints.TryAdd(fingerprint, selection.PortfolioId);
            }
            reports.Add(new(run.SnapshotSha256!, report));
        }
        var selected = input.Reports.Select(r => r.PortfolioId).ToHashSet();
        var omitted = inventory.Where(p => !selected.Contains(p.Id)).Select(p => p.Id).ToArray();
        if (omitted.Length > 0) { issues.Add("OmittedPortfolios"); }
        if (input.ExternalCoverage == "Unknown") { issues.Add("ExternalCoverageUnknown"); }
        if (input.ExternalCoverage == "Provided") { issues.Add("ExternalInputsUnverified"); }
        if (input.PriorLossCoverage == "Unknown") { issues.Add("PriorLossCoverageUnknown"); }
        if (input.PriorLossCoverage == "Provided") { issues.Add("PriorLossClaimsNotApplied"); }
        var totals = reports.Select(r => r.Report.Totals).ToArray();
        var subtotal = new GainTotals(AnnualAmounts.Sum(totals.Select(t => t.GrossDifferenceUsd)),
            AnnualAmounts.Sum(totals.Select(t => t.GrossDifferenceUah)), AnnualAmounts.Sum(totals.Select(t => t.ExpensesUsd)),
            AnnualAmounts.Sum(totals.Select(t => t.ExpensesUah)), AnnualAmounts.Sum(totals.Select(t => t.ProfitUsd)),
            AnnualAmounts.Sum(totals.Select(t => t.ProfitUah)));
        string? externalSubtotal = input.ExternalCoverage == "Unknown" ? null :
            AnnualAmounts.Sum(input.ExternalInvestments.Select(e => AnnualAmounts.CanonicalInput(e.ProfitUah)));
        var document = new AnnualSummaryDocument(Guid.CreateVersion7(), ownerId, ownerId, input.TaxYear, captured,
            AnnualSummarySnapshot.SchemaVersion, "Draft", false, input, inventory, omitted, reports, subtotal,
            externalSubtotal, issues.Order(StringComparer.Ordinal).ToArray());
        string json = ReportSnapshot.Serialize(document);
        if (Encoding.UTF8.GetByteCount(json) > AnnualSummarySnapshot.MaxBytes)
        { throw new AnnualSummaryValidationException(AnnualSummaryError.TooLarge); }
        return new(new(document.Id, ownerId, input.TaxYear, captured, document.SchemaVersion, ReportSnapshot.Hash(json), json), document);
    }

    private static bool Text(string? value, int max) => TradingValidation.IsTextValid(value, max);
    private static bool CoverageValid(string state, int count) => state switch
    {
        "Unknown" or "None" => count == 0,
        "Provided" => count > 0,
        _ => false
    };
    private static bool SamePolicies(TaxReportDocument left, TaxReportDocument right) =>
        left.SchemaVersion == right.SchemaVersion && left.CalculationVersion == right.CalculationVersion
        && left.YearPolicyVersion == right.YearPolicyVersion && left.RoundingPolicyVersion == right.RoundingPolicyVersion;
}
