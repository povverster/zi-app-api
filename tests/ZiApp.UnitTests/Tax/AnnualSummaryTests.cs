using ZiApp.Application.Ledger;
using ZiApp.Application.Reports;
using ZiApp.Domain.TaxReports;

namespace ZiApp.UnitTests.Tax;

public sealed class AnnualSummaryTests
{
    private static readonly DateTimeOffset Captured = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("10000", "-4000", "6000")]
    [InlineData("-10000", "-4000", "-14000")]
    [InlineData("0.3333333333333333333333333333", "0.0000000000000000000000000001", "0.3333333333333333333333333334")]
    [InlineData("79228162514264337593543950335", "-79228162514264337593543950335", "0")]
    public void ExactSumsPreserveSignsAndAllDigits(string first, string second, string expected)
    {
        Assert.Equal(expected, AnnualAmounts.Sum([first, second]));
    }

    [Theory]
    [InlineData("79228162514264337593543950335", "1")]
    [InlineData("79228162514264337593543950335", "0.1")]
    public void OverflowAndSilentDecimalRoundingAreRejected(string first, string second)
    {
        Assert.Equal(AnnualSummaryError.ArithmeticNotRepresentable,
            Assert.Throws<AnnualSummaryValidationException>(() => AnnualAmounts.Sum([first, second])).Error);
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("NaN")]
    [InlineData("01")]
    public void NonCanonicalSourceValuesAreRejected(string value)
    {
        Assert.Equal(AnnualSummaryError.InvalidSource,
            Assert.Throws<AnnualSummaryValidationException>(() => AnnualAmounts.Sum([value])).Error);
    }

    [Fact]
    public void ClaimsAndExternalAmountsNeverReduceSelectedSubtotal()
    {
        var first = Source("10000");
        var second = Source("-4000");
        var input = Input(first, second) with
        {
            ExternalCoverage = "Provided",
            ExternalInvestments = [new("external-1", "Outside broker", "Stock sales 2025", "-500", "statement page 2")],
            PriorLossCoverage = "Provided",
            PriorLossClaims = [new(2024, "9000", "1000", "2024 return")]
        };
        var built = Build(input, first, second);
        Assert.Equal("6000", built.Document.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal("-500", built.Document.ExternalProfitSubtotalUah);
        Assert.Contains("PriorLossClaimsNotApplied", built.Document.ReviewIssues);
        Assert.Contains("ExternalInputsUnverified", built.Document.ReviewIssues);
        Assert.False(built.Document.IsTaxReady);
        Assert.Equal("Draft", built.Document.Status);
        Assert.Equal(built.Draft.SnapshotJson, ReportSnapshot.Serialize(AnnualSummarySnapshot.Read(built.Draft).Value));
    }

    [Theory]
    [InlineData("Unknown", null)]
    [InlineData("None", "0")]
    public void EmptySelectionAndCoverageKeepUnknownDistinctFromZero(string coverage, string? expected)
    {
        var built = Build(Input() with { ExternalCoverage = coverage });
        Assert.Equal("0", built.Document.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal(expected, built.Document.ExternalProfitSubtotalUah);
        Assert.Empty(built.Document.SourceReports);
        Assert.Contains("PriorLossCoverageUnknown", built.Document.ReviewIssues);
        Assert.Contains("LegalReviewPending", built.Document.ReviewIssues);
    }

    [Theory]
    [InlineData("year")]
    [InlineData("duplicate")]
    [InlineData("coverage")]
    [InlineData("overlap")]
    [InlineData("provided-empty")]
    [InlineData("none-nonempty")]
    [InlineData("invalid-claim")]
    [InlineData("used-too-much")]
    [InlineData("numeric-exponent")]
    [InlineData("duplicate-external")]
    [InlineData("too-many")]
    [InlineData("null-lists")]
    public void InvalidSelectionsClaimsAndCoverageAreRejected(string mode)
    {
        var source = Source("1");
        var input = Input(source);
        var external = new ExternalInvestmentInput("ref", "Broker", "2025", "1", "statement");
        input = mode switch
        {
            "year" => input with { TaxYear = 2024 },
            "duplicate" => input with { Reports = [input.Reports[0], input.Reports[0]] },
            "coverage" => input with { PortfolioCoverageConfirmed = false },
            "overlap" => input with { NoOverlapConfirmed = false },
            "provided-empty" => input with { ExternalCoverage = "Provided" },
            "none-nonempty" => input with { ExternalCoverage = "None", ExternalInvestments = [external] },
            "invalid-claim" => input with { PriorLossCoverage = "Provided", PriorLossClaims = [new(2025, "1", "0", "ref")] },
            "used-too-much" => input with { PriorLossCoverage = "Provided", PriorLossClaims = [new(2024, "1", "2", "ref")] },
            "numeric-exponent" => input with { ExternalCoverage = "Provided", ExternalInvestments = [external with { ProfitUah = "1e5" }] },
            "duplicate-external" => input with { ExternalCoverage = "Provided", ExternalInvestments = [external, external with { Reference = " REF " }] },
            "too-many" => input with { Reports = Enumerable.Range(0, 21).Select(_ => new AnnualReportSelection(Guid.CreateVersion7(), Guid.CreateVersion7(), null)).ToArray() },
            _ => input with { Reports = null! }
        };
        Assert.False(AnnualSummaryBuilder.ValidInput(input));
    }

    [Theory]
    [InlineData("calculation")]
    [InlineData("year")]
    [InlineData("rounding")]
    public void MixedPoliciesCannotBeBlended(string policy)
    {
        var first = Source("1");
        var second = Document("2");
        second = policy switch
        {
            "calculation" => second with { CalculationVersion = "different" },
            "year" => second with { YearPolicyVersion = "different" },
            _ => second with { RoundingPolicyVersion = "different" }
        };
        var run = Seal(second);
        Assert.Equal(AnnualSummaryError.IncompatiblePolicies,
            Assert.Throws<AnnualSummaryValidationException>(() => Build(Input(first, run), first, run)).Error);
    }

    [Theory]
    [InlineData("trade-id")]
    [InlineData("fifo-id")]
    [InlineData("same-broker")]
    public void KnownSourceOverlapIsRejected(string mode)
    {
        Guid trade = Guid.CreateVersion7(), fifo = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        var first = Document("1");
        var second = Document("2");
        ReportTrade Row(Guid id, Guid key) => new(id, instrument, key, "Sell", Captured, "2025-01-01T12:00:00Z",
            new(2025, 1, 1), "1", "1", "0", "broker-execution", null, null, null, null, null);
        first = WithTrades(first, Row(trade, fifo));
        second = WithTrades(second, Row(mode == "trade-id" ? trade : Guid.CreateVersion7(), mode == "fifo-id" ? fifo : Guid.CreateVersion7()));
        var runs = new[] { Seal(first), Seal(second) };
        var input = Input(runs);
        if (mode != "same-broker")
        { input = input with { Reports = input.Reports.Select(r => r with { BrokerAccountReference = null }).ToArray() }; }
        Assert.Equal(AnnualSummaryError.KnownOverlap,
            Assert.Throws<AnnualSummaryValidationException>(() => Build(input, runs)).Error);
    }

    [Fact]
    public void IdenticalExecutionValuesInDifferentBrokerAccountsOnlyWarn()
    {
        Guid instrument = Guid.CreateVersion7();
        ReportTrade Row()
        {
            Guid id = Guid.CreateVersion7();
            return new(id, instrument, id, "Sell", Captured, null, new(2025, 1, 1),
                "1", "1", "0", "same-local-id", null, null, null, null, null);
        }
        var runs = new[] { Seal(WithTrades(Document("1"), Row())), Seal(WithTrades(Document("2"), Row())) };
        var input = Input(runs) with { Reports = runs.Select(r => new AnnualReportSelection(r.PortfolioId, r.Id, r.PortfolioId.ToString())).ToArray() };
        Assert.Contains("PossibleDuplicateTrades", Build(input, runs).Document.ReviewIssues);
    }

    [Fact]
    public void LegacyBadHashAndWrongYearSourcesFail()
    {
        var run = Source("1");
        var legacy = new TaxCalculationRun(run.Id, run.PortfolioId, 2025, run.CalculationVersion, Captured);
        Assert.Equal(AnnualSummaryError.LegacySnapshotUnavailable,
            Assert.Throws<AnnualSummaryValidationException>(() => Build(Input(legacy), legacy)).Error);
        legacy.SealDraftSnapshot(ReportSnapshot.SchemaVersion, run.InputSha256!, new string('A', 64), run.SnapshotJson!);
        Assert.Equal(AnnualSummaryError.InvalidSource,
            Assert.Throws<AnnualSummaryValidationException>(() => Build(Input(legacy), legacy)).Error);
        var wrongYear = Seal(Document("1") with { TaxYear = 2024 });
        Assert.Equal(AnnualSummaryError.InvalidInput,
            Assert.Throws<AnnualSummaryValidationException>(() => Build(Input(wrongYear), wrongYear)).Error);
    }

    [Fact]
    public void AnnualIntegrityCheckRejectsModifiedBytesAndOwnerMetadata()
    {
        var built = Build(Input());
        var draft = built.Draft;
        var damaged = new AnnualPreparationDraft(draft.Id, draft.OwnerAccountId, 2025, Captured,
            draft.SchemaVersion, draft.SnapshotSha256, draft.SnapshotJson + " ");
        Assert.Equal(AnnualSummaryError.InvalidSnapshot, AnnualSummarySnapshot.Read(damaged).Error);
        var wrongOwner = new AnnualPreparationDraft(draft.Id, Guid.CreateVersion7(), 2025, Captured,
            draft.SchemaVersion, draft.SnapshotSha256, draft.SnapshotJson);
        Assert.Equal(AnnualSummaryError.InvalidSnapshot, AnnualSummarySnapshot.Read(wrongOwner).Error);
    }

    private static BuiltAnnualSummary Build(CreateAnnualSummaryInput input, params TaxCalculationRun[] runs) =>
        AnnualSummaryBuilder.Build(Guid.CreateVersion7(), input,
            runs.Select(r => new AnnualPortfolio(r.PortfolioId, "Portfolio", true)).ToArray(), runs, Captured);
    private static CreateAnnualSummaryInput Input(params TaxCalculationRun[] runs) => new(2025,
        runs.Select(r => new AnnualReportSelection(r.PortfolioId, r.Id, "broker-account")).ToArray(),
        "Unknown", [], "Unknown", [], true, true);
    private static TaxCalculationRun Source(string profit) => Seal(Document(profit));
    private static TaxReportDocument Document(string profit)
    {
        Guid portfolio = Guid.CreateVersion7();
        var inputs = new ReportInputs(new(portfolio, "Portfolio", "USD"), [], [], [], []);
        return new(Guid.CreateVersion7(), portfolio, 2025, Captured, null, "Draft", false, ReportSnapshot.SchemaVersion,
            "fifo-uah-v2-remaining-cost", ReportSnapshot.YearPolicyVersion, ReportSnapshot.RoundingPolicyVersion,
            ReportSnapshot.InputHash(inputs), inputs, [], new GainTotals(profit, profit, "0", "0", profit, profit));
    }
    private static TaxReportDocument WithTrades(TaxReportDocument document, params ReportTrade[] trades)
    {
        var inputs = document.Inputs with { Trades = trades };
        return document with { Inputs = inputs, InputSha256 = ReportSnapshot.InputHash(inputs) };
    }
    private static TaxCalculationRun Seal(TaxReportDocument document)
    {
        var run = new TaxCalculationRun(document.Id, document.PortfolioId, document.TaxYear, document.CalculationVersion, document.CreatedAtUtc);
        string json = ReportSnapshot.Serialize(document);
        run.SealDraftSnapshot(document.SchemaVersion, document.InputSha256, ReportSnapshot.Hash(json), json);
        return run;
    }
}
