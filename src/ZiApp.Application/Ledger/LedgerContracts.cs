using System.Globalization;

using ZiApp.Application.Trading;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.Transactions;
using ZiApp.Domain.Tax;

namespace ZiApp.Application.Ledger;

public enum LedgerError { AccountUnavailable, NotFound, InvalidInput, UnsupportedCurrency, Duplicate, AlreadyCorrected, InvalidLedger }
public sealed record LedgerResult<T>(T? Value, LedgerError? Error = null) where T : class;
public static class LedgerDecimal
{
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}

public sealed record SplitInput(string EffectiveAt, string Numerator, string Denominator, string SourceReference);
public sealed record CorrectSplitInput(SplitInput Replacement, string Reason);
public sealed record SplitDetails(Guid Id, Guid InstrumentId, Guid FifoOrderId, DateTimeOffset EffectiveAtUtc,
    string? EffectiveAtOriginal, string Numerator, string Denominator, string? SourceReference,
    Guid? RecordedByAccountId, DateTimeOffset? RecordedAtUtc, Guid? PreviousSplitId, string? CorrectionReason, bool IsSuperseded)
{
    public static SplitDetails From(StockSplit split) => new(split.Id, split.InstrumentId, split.FifoOrderId,
        split.EffectiveAtUtc, split.EffectiveAtOriginal, LedgerDecimal.Format(split.Numerator), LedgerDecimal.Format(split.Denominator),
        split.SourceReference, split.RecordedByAccountId, split.RecordedAtUtc, split.PreviousSplitId, split.CorrectionReason, split.IsSuperseded);
}

public interface ISplitRepository
{
    Task<TradingPage<SplitDetails>?> ListAsync(Guid instrumentId, int page, int pageSize, bool includeSuperseded, CancellationToken cancellationToken);
    Task<SplitDetails?> FindAsync(Guid instrumentId, Guid id, CancellationToken cancellationToken);
    Task<LedgerResult<SplitDetails>> RecordAsync(Guid actorId, Guid instrumentId, Guid? originalId,
        DateTimeOffset effectiveAt, SplitInput input, decimal numerator, decimal denominator, string? reason, CancellationToken cancellationToken);
}

public sealed record HoldingsLedger(Portfolio Portfolio, IReadOnlyList<InvestmentTransaction> Trades, IReadOnlyList<StockSplit> Splits);
public interface IHoldingsRepository
{
    Task<HoldingsLedger?> LoadAsync(Guid ownerId, Guid portfolioId, DateTimeOffset asOfUtc, CancellationToken cancellationToken);
}
public sealed record RateBlocker(Guid TradeId, string Status);
public sealed record OpenLotDetails(Guid PurchaseTradeId, Guid ExchangeRateId, string Quantity,
    string PurchaseCostUsd, string PurchaseCostUah, string PurchaseFeeUsd, string PurchaseFeeUah);
public sealed record MatchDetails(Guid PurchaseTradeId, Guid SaleTradeId, Guid PurchaseRateId, Guid SaleRateId,
    string Quantity, string PurchaseCostUsd, string PurchaseCostUah, string SaleProceedsUsd, string SaleProceedsUah,
    string PurchaseFeeUsd, string PurchaseFeeUah, string SaleFeeUsd, string SaleFeeUah, string ProfitUsd, string ProfitUah);
public sealed record GainTotals(string GrossDifferenceUsd, string GrossDifferenceUah, string ExpensesUsd, string ExpensesUah,
    string ProfitUsd, string ProfitUah)
{
    public static GainTotals From(IEnumerable<RealizedTaxLotMatch> matches)
    {
        var list = matches.ToList();
        return new(LedgerDecimal.Format(list.Sum(x => x.GrossDifferenceUsd)), LedgerDecimal.Format(list.Sum(x => x.GrossDifferenceUah)),
            LedgerDecimal.Format(list.Sum(x => x.ExpensesUsd)), LedgerDecimal.Format(list.Sum(x => x.ExpensesUah)),
            LedgerDecimal.Format(list.Sum(x => x.ProfitUsd)), LedgerDecimal.Format(list.Sum(x => x.ProfitUah)));
    }
}
public sealed record PositionDetails(InstrumentDetails Instrument, string Quantity, string Status, IReadOnlyList<RateBlocker> RateBlockers,
    IReadOnlyList<Guid> SplitIds, IReadOnlyList<OpenLotDetails>? OpenLots, IReadOnlyList<MatchDetails>? RealizedMatches, GainTotals? RealizedTotals);
public sealed record HoldingsDetails(Guid PortfolioId, DateTimeOffset AsOfUtc, string CalculationVersion,
    bool IsComplete, bool IsTaxReady, IReadOnlyList<PositionDetails> Positions, GainTotals? RealizedTotals);