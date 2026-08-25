namespace ZiApp.Domain.Tax;

public static class FifoRealizedGainCalculator
{
    public static RealizedGainResult Calculate(
        IEnumerable<PurchaseTaxLot> purchases,
        IEnumerable<SaleTaxTransaction> sales,
        IEnumerable<StockSplitEvent>? stockSplits = null)
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
            .ThenBy(item => item.Id, StringComparer.Ordinal);

        var openLots = new List<OpenLot>();
        var matches = new List<RealizedTaxLotMatch>();

        foreach (var ledgerEvent in events)
        {
            if (ledgerEvent.Purchase is not null)
            {
                openLots.Add(OpenLot.FromPurchase(ledgerEvent.Purchase));
                continue;
            }

            if (ledgerEvent.Split is not null)
            {
                ApplySplit(openLots, ledgerEvent.Split);
                continue;
            }

            MatchSale(openLots, ledgerEvent.Sale!, matches);
        }

        return new RealizedGainResult(matches.AsReadOnly());
    }

    private static void MatchSale(
        IEnumerable<OpenLot> openLots,
        SaleTaxTransaction sale,
        List<RealizedTaxLotMatch> matches)
    {
        var quantityRemaining = sale.Quantity;
        var saleFeePerUnitUsd = sale.FeeUsd / sale.Quantity;

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
            var purchaseCostUsd = lot.UnitCostUsd * matchedQuantity;
            var saleProceedsUsd = sale.UnitPriceUsd * matchedQuantity;
            var purchaseFeeUsd = lot.PurchaseFeePerUnitUsd * matchedQuantity;
            var saleFeeUsd = saleFeePerUnitUsd * matchedQuantity;

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
            quantityRemaining -= matchedQuantity;
        }

        if (quantityRemaining > 0m)
        {
            throw new InvalidOperationException(
                $"Sale '{sale.Id}' exceeds the available FIFO quantity by {quantityRemaining}.");
        }
    }

    private static void ApplySplit(IEnumerable<OpenLot> openLots, StockSplitEvent split)
    {
        var factor = split.Numerator / split.Denominator;

        foreach (var lot in openLots)
        {
            lot.QuantityRemaining *= factor;
            lot.UnitCostUsd /= factor;
            lot.PurchaseFeePerUnitUsd /= factor;
        }
    }

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

        public static OpenLot FromPurchase(PurchaseTaxLot purchase)
        {
            return new OpenLot(
                purchase.Id,
                purchase.Quantity,
                purchase.UnitPriceUsd,
                purchase.FeeUsd / purchase.Quantity,
                purchase.UsdToUahRate);
        }
    }

    private sealed record LedgerEvent(
        string Id,
        DateTimeOffset ExecutedAt,
        PurchaseTaxLot? Purchase,
        SaleTaxTransaction? Sale,
        StockSplitEvent? Split)
    {
        public static LedgerEvent ForPurchase(PurchaseTaxLot purchase) =>
            new(purchase.Id, purchase.ExecutedAt, purchase, null, null);

        public static LedgerEvent ForSale(SaleTaxTransaction sale) =>
            new(sale.Id, sale.ExecutedAt, null, sale, null);

        public static LedgerEvent ForSplit(StockSplitEvent split) =>
            new(split.Id, split.ExecutedAt, null, null, split);
    }
}