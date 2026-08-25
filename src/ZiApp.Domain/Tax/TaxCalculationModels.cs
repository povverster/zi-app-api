namespace ZiApp.Domain.Tax;

public sealed record PurchaseTaxLot(
    string Id,
    DateTimeOffset ExecutedAt,
    decimal Quantity,
    decimal UnitPriceUsd,
    decimal FeeUsd,
    decimal UsdToUahRate);

public sealed record SaleTaxTransaction(
    string Id,
    DateTimeOffset ExecutedAt,
    decimal Quantity,
    decimal UnitPriceUsd,
    decimal FeeUsd,
    decimal UsdToUahRate);

public sealed record StockSplitEvent(
    string Id,
    DateTimeOffset ExecutedAt,
    decimal Numerator,
    decimal Denominator);

public sealed record RealizedTaxLotMatch(
    string PurchaseLotId,
    string SaleId,
    decimal MatchedQuantity,
    decimal PurchaseCostUsd,
    decimal PurchaseCostUah,
    decimal SaleProceedsUsd,
    decimal SaleProceedsUah,
    decimal PurchaseFeeUsd,
    decimal PurchaseFeeUah,
    decimal SaleFeeUsd,
    decimal SaleFeeUah)
{
    public decimal GrossDifferenceUsd => SaleProceedsUsd - PurchaseCostUsd;

    public decimal GrossDifferenceUah => SaleProceedsUah - PurchaseCostUah;

    public decimal ExpensesUsd => PurchaseFeeUsd + SaleFeeUsd;

    public decimal ExpensesUah => PurchaseFeeUah + SaleFeeUah;

    public decimal ProfitUsd => GrossDifferenceUsd - ExpensesUsd;

    public decimal ProfitUah => GrossDifferenceUah - ExpensesUah;
}

public sealed class RealizedGainResult
{
    internal RealizedGainResult(IReadOnlyList<RealizedTaxLotMatch> matches)
    {
        Matches = matches;
    }

    public IReadOnlyList<RealizedTaxLotMatch> Matches { get; }

    public decimal GrossDifferenceUsd => Matches.Sum(match => match.GrossDifferenceUsd);

    public decimal GrossDifferenceUah => Matches.Sum(match => match.GrossDifferenceUah);

    public decimal ExpensesUsd => Matches.Sum(match => match.ExpensesUsd);

    public decimal ExpensesUah => Matches.Sum(match => match.ExpensesUah);

    public decimal ProfitUsd => Matches.Sum(match => match.ProfitUsd);

    public decimal ProfitUah => Matches.Sum(match => match.ProfitUah);
}