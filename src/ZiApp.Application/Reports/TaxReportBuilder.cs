using System.Text;

using ZiApp.Application.ExchangeRates;
using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Reports;

public static class TaxReportBuilder
{
    public static PreparedReport Prepare(HoldingsLedger ledger, int year)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (ledger.Trades.Count > ReportSnapshot.MaxTrades || ledger.Splits.Count > ReportSnapshot.MaxSplits)
        { throw new ReportValidationException(ReportError.TooLarge); }
        if (ledger.Portfolio.BaseCurrencyCode != "USD") { throw new ReportValidationException(ReportError.UnsupportedCurrency); }
        var current = ledger.Trades.Where(t => !t.IsSuperseded).ToList();
        var dates = current.ToDictionary(t => t.Id, t => TradeRatePolicy.SelectDate(t)
            ?? throw new ReportValidationException(ReportError.InvalidLedger));
        // Only instruments sold in this broker calendar year matter. Include their entire
        // preceding instant-ordered history, not just purchases made during the report year.
        var cutoffs = current.Where(t => t.Side == TradeSide.Sell && dates[t.Id].Year == year)
            .GroupBy(t => t.InstrumentId).ToDictionary(g => g.Key, g => g.Max(t => t.ExecutedAtUtc));
        var trades = current.Where(t => cutoffs.TryGetValue(t.InstrumentId, out var cutoff) && t.ExecutedAtUtc <= cutoff)
            .OrderBy(t => t.ExecutedAtUtc).ThenBy(t => t.FifoOrderId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(t => t.Id).ToList();
        if (trades.Any(t => t.Instrument.CurrencyCode != "USD")) { throw new ReportValidationException(ReportError.UnsupportedCurrency); }
        var splits = ledger.Splits.Where(s => !s.IsSuperseded && cutoffs.TryGetValue(s.InstrumentId, out var cutoff) && s.EffectiveAtUtc <= cutoff)
            .OrderBy(s => s.EffectiveAtUtc).ThenBy(s => s.FifoOrderId.ToString("D"), StringComparer.Ordinal).ThenBy(s => s.Id).ToList();
        var inputs = new ReportInputs(new(ledger.Portfolio.Id, ledger.Portfolio.Name, ledger.Portfolio.BaseCurrencyCode),
            trades.Select(t => t.Instrument).DistinctBy(i => i.Id).OrderBy(i => i.Id).Select(InstrumentDetails.From).ToList(),
            trades.Select(t => new ReportTrade(t.Id, t.InstrumentId, t.FifoOrderId, t.Side.ToString(), t.ExecutedAtUtc,
                t.ExecutedAtOriginal, dates[t.Id], LedgerDecimal.Format(t.Quantity), LedgerDecimal.Format(t.UnitPriceUsd),
                LedgerDecimal.Format(t.FeeUsd), t.BrokerTransactionId, t.ExchangeRateId, t.ExchangeRateSelectedDate,
                t.ExchangeRatePolicy, t.ExchangeRateResolvedByAccountId, t.ExchangeRateResolvedAtUtc)).ToList(),
            splits.Select(SplitDetails.From).ToList(),
            trades.Where(t => t.ExchangeRate is not null).Select(t => t.ExchangeRate!).DistinctBy(r => r.Id).OrderBy(r => r.Id)
                .Select(r => new ReportRate(r.Id, r.CurrencyCode, r.EffectiveDate, LedgerDecimal.Format(r.RateToUah), r.Source,
                    r.RetrievedAtUtc, r.CalculationDate, r.SourceUrl, r.ResponseSha256, r.RawResponseJson)).ToList());
        return new(inputs, trades);
    }

    public static BuiltReport Build(HoldingsLedger ledger, int year, DateTimeOffset capturedAtUtc)
    {
        var prepared = Prepare(ledger, year);
        var blockers = prepared.Trades.Where(t => !TradeRatePolicy.HasVerifiedRate(t))
            .Select(t => new RateBlocker(t.Id, t.ExchangeRateId is null ? "Pending" : "LinkedUnverified")).ToList();
        if (blockers.Count > 0) { throw new ReportValidationException(ReportError.UnresolvedRates, blockers); }
        Guid runId = Guid.CreateVersion7();
        var matches = new List<ReportMatch>();
        var rows = new List<TaxLotMatchSnapshot>();
        var selectedMatches = new List<RealizedTaxLotMatch>();
        var source = prepared.Inputs.Trades.ToDictionary(t => t.Id);
        foreach (var group in prepared.Trades.GroupBy(t => t.InstrumentId).OrderBy(g => g.Key))
        {
            DateTimeOffset through = group.Max(t => t.ExecutedAtUtc);
            var purchases = group.Where(t => t.Side == TradeSide.Buy).Select(t => new PurchaseTaxLot(t.Id.ToString("D"),
                t.ExecutedAtUtc, t.Quantity, t.UnitPriceUsd, t.FeeUsd, t.ExchangeRate!.RateToUah, t.FifoOrderId.ToString("D")));
            var sales = group.Where(t => t.Side == TradeSide.Sell).Select(t => new SaleTaxTransaction(t.Id.ToString("D"),
                t.ExecutedAtUtc, t.Quantity, t.UnitPriceUsd, t.FeeUsd, t.ExchangeRate!.RateToUah, t.FifoOrderId.ToString("D")));
            var splits = ledger.Splits.Where(s => !s.IsSuperseded && s.InstrumentId == group.Key && s.EffectiveAtUtc <= through)
                .Select(s => new StockSplitEvent(s.Id.ToString("D"), s.EffectiveAtUtc, s.Numerator, s.Denominator, s.FifoOrderId.ToString("D")));
            var result = FifoRealizedGainCalculator.CalculateHoldings(purchases, sales, splits);
            foreach (var match in result.Matches)
            {
                Guid buy = Guid.Parse(match.PurchaseLotId), sale = Guid.Parse(match.SaleId);
                if (source[sale].BrokerDate.Year != year) { continue; }
                rows.Add(new(Guid.CreateVersion7(), runId, buy, sale, match));
                selectedMatches.Add(match);
                matches.Add(new(group.Key, new(buy, sale, source[buy].ExchangeRateId!.Value, source[sale].ExchangeRateId!.Value,
                    LedgerDecimal.Format(match.MatchedQuantity), LedgerDecimal.Format(match.PurchaseCostUsd),
                    LedgerDecimal.Format(match.PurchaseCostUah), LedgerDecimal.Format(match.SaleProceedsUsd),
                    LedgerDecimal.Format(match.SaleProceedsUah), LedgerDecimal.Format(match.PurchaseFeeUsd),
                    LedgerDecimal.Format(match.PurchaseFeeUah), LedgerDecimal.Format(match.SaleFeeUsd),
                    LedgerDecimal.Format(match.SaleFeeUah), LedgerDecimal.Format(match.ProfitUsd),
                    LedgerDecimal.Format(match.ProfitUah)), GainTotals.From([match])));
            }
        }
        string inputHash = ReportSnapshot.InputHash(prepared.Inputs);
        var document = new TaxReportDocument(runId, ledger.Portfolio.Id, year, capturedAtUtc,
            prepared.Trades.Count == 0 ? null : prepared.Trades.Max(t => t.ExecutedAtUtc),
            "Draft", false, ReportSnapshot.SchemaVersion, FifoRealizedGainCalculator.HoldingsVersion,
            ReportSnapshot.YearPolicyVersion, ReportSnapshot.RoundingPolicyVersion, inputHash, prepared.Inputs,
            matches, GainTotals.From(selectedMatches));
        string json = ReportSnapshot.Serialize(document);
        if (Encoding.UTF8.GetByteCount(json) > ReportSnapshot.MaxBytes) { throw new ReportValidationException(ReportError.TooLarge); }
        var run = new TaxCalculationRun(runId, ledger.Portfolio.Id, year, document.CalculationVersion, capturedAtUtc);
        run.SealDraftSnapshot(ReportSnapshot.SchemaVersion, inputHash, ReportSnapshot.Hash(json), json);
        return new(run, rows, document);
    }
}
