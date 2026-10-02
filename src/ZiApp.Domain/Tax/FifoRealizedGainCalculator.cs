using ZiApp.Domain.Instruments;

namespace ZiApp.Domain.Tax;

public static class FifoRealizedGainCalculator
{
    public const string HoldingsVersion = "fifo-uah-v2-remaining-cost";

    public static RealizedGainResult Calculate(
        IEnumerable<PurchaseTaxLot> purchases,
        IEnumerable<SaleTaxTransaction> sales,
        IEnumerable<StockSplitEvent>? stockSplits = null) => CalculateCore(purchases, sales, stockSplits, false);

    // Keep the original v1 entry point for historical calculation reproducibility.
    public static RealizedGainResult CalculateHoldings(
        IEnumerable<PurchaseTaxLot> purchases,
        IEnumerable<SaleTaxTransaction> sales,
        IEnumerable<StockSplitEvent>? stockSplits = null) => CalculateCore(purchases, sales, stockSplits, true);

    private static RealizedGainResult CalculateCore(IEnumerable<PurchaseTaxLot> purchases,
        IEnumerable<SaleTaxTransaction> sales, IEnumerable<StockSplitEvent>? stockSplits, bool preserveRemainingCost)
    {
        ArgumentNullException.ThrowIfNull(purchases);
        ArgumentNullException.ThrowIfNull(sales);

        var purchaseList = purchases.ToList();
        var saleList = sales.ToList();
        var splitList = stockSplits?.ToList() ?? [];

        ValidateInputs(purchaseList, saleList, splitList);

        var events = purchaseList
            .Select(purchase => LedgerEvent.ForPurchase(purchase))
            .Concat(saleList.Select(sale => LedgerEvent.ForSale(sale)))
            .Concat(splitList.Select(split => LedgerEvent.ForSplit(split)))
            .OrderBy(item => item.ExecutedAt)
            .ThenBy(item => item.FifoOrderId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal);

        var openLots = new List<OpenLot>();
        var matches = new List<RealizedTaxLotMatch>();

        foreach (var ledgerEvent in events)
        {
            if (ledgerEvent.Purchase is not null)
            {
                openLots.Add(OpenLot.FromPurchase(ledgerEvent.Purchase, preserveRemainingCost));
                continue;
            }

            if (ledgerEvent.Split is not null)
            {
                ApplySplit(openLots, ledgerEvent.Split, preserveRemainingCost);
                continue;
            }

            MatchSale(openLots, ledgerEvent.Sale!, matches, preserveRemainingCost);
        }

        return new RealizedGainResult(matches.AsReadOnly(), !preserveRemainingCost ? [] : openLots.Where(lot => lot.QuantityRemaining > 0m)
            .Select(lot => new OpenTaxLot(lot.PurchaseLotId, lot.QuantityRemaining,
                lot.RemainingCostUsd, lot.RemainingCostUsd * lot.PurchaseUsdToUahRate,
                lot.RemainingFeeUsd, lot.RemainingFeeUsd * lot.PurchaseUsdToUahRate)).ToList().AsReadOnly());
    }

    private static void MatchSale(
        IEnumerable<OpenLot> openLots,
        SaleTaxTransaction sale,
        List<RealizedTaxLotMatch> matches, bool preserveRemainingCost)
    {
        var quantityRemaining = sale.Quantity;
        var saleFeePerUnitUsd = sale.FeeUsd / sale.Quantity;
        decimal remainingSaleFeeUsd = sale.FeeUsd;

        foreach (var lot in openLots)
        {
            if (quantityRemaining == 0m)
            {
                break;
            }

            if (lot.QuantityRemaining == 0m)
            {
                continue;
            }

            var matchedQuantity = Math.Min(quantityRemaining, lot.QuantityRemaining);
            var purchaseCostUsd = preserveRemainingCost
                ? Allocate(lot.RemainingCostUsd, matchedQuantity, lot.QuantityRemaining)
                : lot.UnitCostUsd * matchedQuantity;
            var saleProceedsUsd = sale.UnitPriceUsd * matchedQuantity;
            var purchaseFeeUsd = preserveRemainingCost
                ? Allocate(lot.RemainingFeeUsd, matchedQuantity, lot.QuantityRemaining)
                : lot.PurchaseFeePerUnitUsd * matchedQuantity;
            var saleFeeUsd = preserveRemainingCost
                ? Allocate(remainingSaleFeeUsd, matchedQuantity, quantityRemaining)
                : saleFeePerUnitUsd * matchedQuantity;

            matches.Add(new RealizedTaxLotMatch(
                lot.PurchaseLotId,
                sale.Id,
                matchedQuantity,
                purchaseCostUsd,
                purchaseCostUsd * lot.PurchaseUsdToUahRate,
                saleProceedsUsd,
                saleProceedsUsd * sale.UsdToUahRate,
                purchaseFeeUsd,
                purchaseFeeUsd * lot.PurchaseUsdToUahRate,
                saleFeeUsd,
                saleFeeUsd * sale.UsdToUahRate));

            lot.QuantityRemaining -= matchedQuantity;
            if (preserveRemainingCost)
            {
                lot.RemainingCostUsd -= purchaseCostUsd;
                lot.RemainingFeeUsd -= purchaseFeeUsd;
                remainingSaleFeeUsd -= saleFeeUsd;
            }
            quantityRemaining -= matchedQuantity;
        }

        if (quantityRemaining > 0m)
        {
            throw new InvalidOperationException(
                $"Sale '{sale.Id}' exceeds the available FIFO quantity by {quantityRemaining}.");
        }
    }

    private static void ApplySplit(IEnumerable<OpenLot> openLots, StockSplitEvent split, bool preserveRemainingCost)
    {
        var factor = split.Numerator / split.Denominator;

        foreach (var lot in openLots)
        {
            if (preserveRemainingCost)
            {
                if (lot.QuantityRemaining == 0m) { continue; }
                lot.QuantityRemaining = SplitQuantity.Apply(lot.QuantityRemaining, split.Numerator, split.Denominator);
                continue;
            }
            lot.QuantityRemaining *= factor;
            lot.UnitCostUsd /= factor;
            lot.PurchaseFeePerUnitUsd /= factor;
        }
    }

    private static decimal Allocate(decimal total, decimal quantity, decimal remainingQuantity) =>
        quantity == remainingQuantity ? total : total * quantity / remainingQuantity;

    private static void ValidateInputs(
        IEnumerable<PurchaseTaxLot> purchases,
        IEnumerable<SaleTaxTransaction> sales,
        IEnumerable<StockSplitEvent> splits)
    {
        var eventIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var purchase in purchases)
        {
            ValidateEventId(purchase.Id, eventIds);
            ValidatePositive(purchase.Quantity, nameof(purchase.Quantity));
            ValidatePositive(purchase.UnitPriceUsd, nameof(purchase.UnitPriceUsd));
            ValidateNonNegative(purchase.FeeUsd, nameof(purchase.FeeUsd));
            ValidatePositive(purchase.UsdToUahRate, nameof(purchase.UsdToUahRate));
        }

        foreach (var sale in sales)
        {
            ValidateEventId(sale.Id, eventIds);
            ValidatePositive(sale.Quantity, nameof(sale.Quantity));
            ValidatePositive(sale.UnitPriceUsd, nameof(sale.UnitPriceUsd));
            ValidateNonNegative(sale.FeeUsd, nameof(sale.FeeUsd));
            ValidatePositive(sale.UsdToUahRate, nameof(sale.UsdToUahRate));
        }

        foreach (var split in splits)
        {
            ValidateEventId(split.Id, eventIds);
            ValidatePositive(split.Numerator, nameof(split.Numerator));
            ValidatePositive(split.Denominator, nameof(split.Denominator));
        }
    }

    private static void ValidateEventId(string id, HashSet<string> eventIds)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Every ledger event must have a non-empty stable ID.", nameof(id));
        }

        if (!eventIds.Add(id))
        {
            throw new ArgumentException($"Ledger event ID '{id}' is duplicated.", nameof(id));
        }
    }

    private static void ValidatePositive(decimal value, string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than zero.");
        }
    }

    private static void ValidateNonNegative(decimal value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }
    }

    private sealed class OpenLot
    {
        private OpenLot(
            string purchaseLotId,
            decimal quantityRemaining,
            decimal unitCostUsd,
            decimal purchaseFeePerUnitUsd,
            decimal purchaseUsdToUahRate)
        {
            PurchaseLotId = purchaseLotId;
            QuantityRemaining = quantityRemaining;
            UnitCostUsd = unitCostUsd;
            PurchaseFeePerUnitUsd = purchaseFeePerUnitUsd;
            PurchaseUsdToUahRate = purchaseUsdToUahRate;
        }

        public string PurchaseLotId { get; }

        public decimal QuantityRemaining { get; set; }

        public decimal UnitCostUsd { get; set; }

        public decimal PurchaseFeePerUnitUsd { get; set; }

        public decimal PurchaseUsdToUahRate { get; }
        public decimal RemainingCostUsd { get; set; }
        public decimal RemainingFeeUsd { get; set; }

        public static OpenLot FromPurchase(PurchaseTaxLot purchase, bool preserveRemainingCost)
        {
            return new OpenLot(
                purchase.Id,
                purchase.Quantity,
                purchase.UnitPriceUsd,
                purchase.FeeUsd / purchase.Quantity,
                purchase.UsdToUahRate)
            {
                RemainingCostUsd = preserveRemainingCost ? purchase.Quantity * purchase.UnitPriceUsd : 0m,
                RemainingFeeUsd = preserveRemainingCost ? purchase.FeeUsd : 0m
            };
        }
    }

    private sealed record LedgerEvent(
        string Id,
        string FifoOrderId,
        DateTimeOffset ExecutedAt,
        PurchaseTaxLot? Purchase,
        SaleTaxTransaction? Sale,
        StockSplitEvent? Split)
    {
        public static LedgerEvent ForPurchase(PurchaseTaxLot purchase) =>
            new(purchase.Id, purchase.FifoOrderId ?? purchase.Id, purchase.ExecutedAt, purchase, null, null);

        public static LedgerEvent ForSale(SaleTaxTransaction sale) =>
            new(sale.Id, sale.FifoOrderId ?? sale.Id, sale.ExecutedAt, null, sale, null);

        public static LedgerEvent ForSplit(StockSplitEvent split) =>
            new(split.Id, split.FifoOrderId ?? split.Id, split.ExecutedAt, null, null, split);
    }
}