using ZiApp.Application.ExchangeRates;
using ZiApp.Application.Security;
using ZiApp.Application.Trading;
using ZiApp.Domain.Tax;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Ledger;

public sealed class HoldingsService(ICurrentAccount account, IHoldingsRepository repository, TimeProvider clock)
{
    public async Task<LedgerResult<HoldingsDetails>> GetAsync(Guid portfolioId, string? asOf, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, LedgerError.AccountUnavailable); }
        DateTimeOffset cutoff = clock.GetUtcNow();
        if (asOf is not null && !TradingValidation.TryInstant(asOf, out cutoff)) { return new(null, LedgerError.InvalidInput); }
        var ledger = await repository.LoadAsync(owner.Value, portfolioId, cutoff, cancellationToken);
        if (ledger is null) { return new(null, LedgerError.NotFound); }
        if (ledger.Portfolio.BaseCurrencyCode != "USD" || ledger.Trades.Any(t => t.Instrument.CurrencyCode != "USD"))
        { return new(null, LedgerError.UnsupportedCurrency); }
        try { return new(Project(ledger, cutoff)); }
        catch (Exception error) when (error is OverflowException or InvalidOperationException or ArgumentException)
        { return new(null, LedgerError.InvalidLedger); }
    }

    private static HoldingsDetails Project(HoldingsLedger ledger, DateTimeOffset cutoff)
    {
        var quantities = TradeQuantityValidator.GetHoldings(ledger.Trades, ledger.Splits);
        var positions = new List<PositionDetails>();
        var allMatches = new List<RealizedTaxLotMatch>();
        foreach (var group in ledger.Trades.GroupBy(t => t.InstrumentId).OrderBy(g => g.First().Instrument.Symbol, StringComparer.Ordinal).ThenBy(g => g.Key))
        {
            var trades = group.ToDictionary(t => t.Id);
            var splits = ledger.Splits.Where(s => s.InstrumentId == group.Key).OrderBy(s => s.EffectiveAtUtc).ThenBy(s => s.FifoOrderId).ToList();
            var blockers = group.Where(t => !TradeRatePolicy.HasVerifiedRate(t)).OrderBy(t => t.ExecutedAtUtc).ThenBy(t => t.FifoOrderId)
                .Select(t => new RateBlocker(t.Id, t.ExchangeRateId is null ? "Pending" : "LinkedUnverified")).ToList();
            if (blockers.Count > 0)
            {
                positions.Add(new(InstrumentDetails.From(group.First().Instrument), LedgerDecimal.Format(quantities[group.Key]),
                    "PendingRates", blockers, splits.Select(s => s.Id).ToList(), null, null, null));
                continue;
            }

            var purchases = group.Where(t => t.Side == TradeSide.Buy).Select(t => new PurchaseTaxLot(t.Id.ToString("D"),
                t.ExecutedAtUtc, t.Quantity, t.UnitPriceUsd, t.FeeUsd, t.ExchangeRate!.RateToUah, t.FifoOrderId.ToString("D")));
            var sales = group.Where(t => t.Side == TradeSide.Sell).Select(t => new SaleTaxTransaction(t.Id.ToString("D"),
                t.ExecutedAtUtc, t.Quantity, t.UnitPriceUsd, t.FeeUsd, t.ExchangeRate!.RateToUah, t.FifoOrderId.ToString("D")));
            var result = FifoRealizedGainCalculator.CalculateHoldings(purchases, sales, splits.Select(s =>
                new StockSplitEvent(s.Id.ToString("D"), s.EffectiveAtUtc, s.Numerator, s.Denominator, s.FifoOrderId.ToString("D"))));
            var lots = result.OpenLots.Select(lot => new OpenLotDetails(Guid.Parse(lot.PurchaseLotId),
                trades[Guid.Parse(lot.PurchaseLotId)].ExchangeRateId!.Value, LedgerDecimal.Format(lot.Quantity),
                LedgerDecimal.Format(lot.PurchaseCostUsd), LedgerDecimal.Format(lot.PurchaseCostUah),
                LedgerDecimal.Format(lot.PurchaseFeeUsd), LedgerDecimal.Format(lot.PurchaseFeeUah))).ToList();
            var matches = result.Matches.Select(m => new MatchDetails(Guid.Parse(m.PurchaseLotId), Guid.Parse(m.SaleId),
                trades[Guid.Parse(m.PurchaseLotId)].ExchangeRateId!.Value, trades[Guid.Parse(m.SaleId)].ExchangeRateId!.Value,
                LedgerDecimal.Format(m.MatchedQuantity), LedgerDecimal.Format(m.PurchaseCostUsd), LedgerDecimal.Format(m.PurchaseCostUah),
                LedgerDecimal.Format(m.SaleProceedsUsd), LedgerDecimal.Format(m.SaleProceedsUah), LedgerDecimal.Format(m.PurchaseFeeUsd),
                LedgerDecimal.Format(m.PurchaseFeeUah), LedgerDecimal.Format(m.SaleFeeUsd), LedgerDecimal.Format(m.SaleFeeUah),
                LedgerDecimal.Format(m.ProfitUsd), LedgerDecimal.Format(m.ProfitUah))).ToList();
            positions.Add(new(InstrumentDetails.From(group.First().Instrument), LedgerDecimal.Format(quantities[group.Key]),
                "Complete", blockers, splits.Select(s => s.Id).ToList(), lots, matches, GainTotals.From(result.Matches)));
            allMatches.AddRange(result.Matches);
        }
        bool complete = positions.All(p => p.Status == "Complete");
        return new(ledger.Portfolio.Id, cutoff, FifoRealizedGainCalculator.HoldingsVersion, complete, false,
            positions, complete ? GainTotals.From(allMatches) : null);
    }

}
