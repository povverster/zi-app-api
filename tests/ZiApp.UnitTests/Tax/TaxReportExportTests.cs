using System.Text;

using Microsoft.VisualBasic.FileIO;

using ZiApp.Application.Ledger;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.TaxReports;

namespace ZiApp.UnitTests.Tax;

public sealed class TaxReportExportTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://example.test\",\"open\")")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    [InlineData("\r\n=1+1")]
    [InlineData("Київ, \"портфель\"")]
    [InlineData("＝1+1")]
    public void CsvQuotesAndNeutralizesAllUserTextWithoutLosingDigits(string text)
    {
        var document = Document(text);
        byte[] bytes = TaxReportCsv.Export(document);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes[3..]));
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields()!;
        var row = parser.ReadFields()!;
        var totals = parser.ReadFields()!;
        Assert.Equal(35, header.Length);
        Assert.Equal(header.Length, row.Length);
        Assert.Equal(header.Length, totals.Length);
        Assert.Equal("'" + text, row[3]);
        Assert.Equal("'" + text, row[12]);
        Assert.Equal("'" + text, row[13]);
        Assert.Equal(document.Matches[0].Values.PurchaseCostUsd, row[21]);
        Assert.Equal("Draft", row[5]);
        Assert.Equal("false", row[6]);
        Assert.Equal("Total", totals[0]);
        Assert.Equal(document.Totals.ProfitUah, totals[34]);
        Assert.True(parser.EndOfData);
    }

    [Fact]
    public void SnapshotRoundTripChecksDigestsAndCannotBeOverwritten()
    {
        var document = Document("Portfolio");
        var run = new TaxCalculationRun(document.Id, document.PortfolioId, document.TaxYear, document.CalculationVersion, document.CreatedAtUtc);
        string json = ReportSnapshot.Serialize(document);
        run.SealDraftSnapshot(document.SchemaVersion, document.InputSha256, ReportSnapshot.Hash(json), json);
        Assert.Equal(json, ReportSnapshot.Serialize(ReportSnapshot.Read(run).Value));
        Assert.Throws<InvalidOperationException>(() => run.SealDraftSnapshot(document.SchemaVersion, document.InputSha256, ReportSnapshot.Hash(json), json));
        var bad = new TaxCalculationRun(document.Id, document.PortfolioId, document.TaxYear, document.CalculationVersion, document.CreatedAtUtc);
        bad.SealDraftSnapshot(document.SchemaVersion, document.InputSha256, new string('A', 64), json);
        Assert.Equal(ReportError.InvalidSnapshot, ReportSnapshot.Read(bad).Error);
    }

    [Fact]
    public void LegacyRunsAreNotSilentlyReconstructedFromCurrentInputs()
    {
        var legacy = new TaxCalculationRun(Guid.CreateVersion7(), Guid.CreateVersion7(), 2025, "fifo-uah-v1", DateTimeOffset.UtcNow);
        Assert.Equal(ReportError.LegacySnapshotUnavailable, ReportSnapshot.Read(legacy).Error);
    }

    private static TaxReportDocument Document(string text)
    {
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        Guid buy = Guid.CreateVersion7(), sale = Guid.CreateVersion7(), buyRate = Guid.CreateVersion7(), saleRate = Guid.CreateVersion7();
        DateTimeOffset time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var inputs = new ReportInputs(new(portfolio, text, "USD"),
            [new InstrumentDetails(instrument, text, text, "Stock", InstrumentType.Stock, "USD", null)],
            [Trade(buy, "Buy", buyRate), Trade(sale, "Sell", saleRate)], [], []);
        ReportTrade Trade(Guid id, string side, Guid rate) => new(id, instrument, id, side, time, "2025-01-01T12:00:00Z",
            new(2025, 1, 1), "1", "100", "0", null, rate, new(2025, 1, 1), "policy", Guid.Empty, time);
        var totals = new GainTotals("16.666666666666666666666666667", "700", "1.3333333333333333333333333333", "50", "15.333333333333333333333333334", "650");
        var match = new ReportMatch(instrument,
            new MatchDetails(buy, sale, buyRate, saleRate, "1", "33.333333333333333333333333333", "1400", "50", "2100",
                "0.3333333333333333333333333333", "10", "1", "40", totals.ProfitUsd, totals.ProfitUah), totals);
        return new(Guid.CreateVersion7(), portfolio, 2025, time, time, "Draft", false,
            ReportSnapshot.SchemaVersion, "fifo-uah-v2-remaining-cost", ReportSnapshot.YearPolicyVersion,
            ReportSnapshot.RoundingPolicyVersion, ReportSnapshot.InputHash(inputs), inputs, [match], totals);
    }
}
