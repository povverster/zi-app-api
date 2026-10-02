using ZiApp.Domain.Instruments;

namespace ZiApp.Domain.Transactions;

/// <summary>Checks chronological holdings without inventing exchange rates or tax results.</summary>
public static class TradeQuantityValidator
{
    public static bool CanExecute(IEnumerable<InvestmentTransaction> trades, IEnumerable<StockSplit> splits)
    {
        try { GetHoldings(trades, splits); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public static IReadOnlyDictionary<Guid, decimal> GetHoldings(IEnumerable<InvestmentTransaction> trades, IEnumerable<StockSplit> splits)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(splits);
        var splitList = splits.Where(split => !split.IsSuperseded).ToList();
        var holdings = new Dictionary<Guid, decimal>();
        foreach (var group in trades.Where(trade => !trade.IsSuperseded).GroupBy(trade => trade.InstrumentId))
        {
            var events = group.Select(trade => new QuantityEvent(trade.FifoOrderId.ToString("D"), trade.ExecutedAtUtc, trade, null))
                .Concat(splitList.Where(split => split.InstrumentId == group.Key)
                    .Select(split => new QuantityEvent(split.FifoOrderId.ToString("D"), split.EffectiveAtUtc, null, split)))
                .OrderBy(item => item.Time).ThenBy(item => item.Id, StringComparer.Ordinal);
            // Keep individual lots so split rounding matches the FIFO calculator.
            var lots = new List<decimal>();
            foreach (var item in events)
            {
                if (item.Split is not null)
                {
                    for (int i = 0; i < lots.Count; i++)
                    {
                        lots[i] = SplitQuantity.Apply(lots[i], item.Split.Numerator, item.Split.Denominator);
                    }
                }
                else if (item.Trade!.Side == TradeSide.Buy)
                {
                    lots.Add(item.Trade.Quantity);
                }
                else
                {
                    decimal remaining = item.Trade.Quantity;
                    for (int i = 0; i < lots.Count && remaining > 0m; i++)
                    {
                        decimal matched = Math.Min(lots[i], remaining);
                        lots[i] -= matched;
                        remaining -= matched;
                    }

                    if (remaining > 0m)
                    {
                        throw new InvalidOperationException("A sale exceeds available holdings.");
                    }
                }
            }
            holdings[group.Key] = lots.Sum();
        }

        return holdings;
    }

    private sealed record QuantityEvent(string Id, DateTimeOffset Time, InvestmentTransaction? Trade, StockSplit? Split);
}