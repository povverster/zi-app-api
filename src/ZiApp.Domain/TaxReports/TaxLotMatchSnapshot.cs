using ZiApp.Domain.Common;
using ZiApp.Domain.Tax;
using ZiApp.Domain.Transactions;

namespace ZiApp.Domain.TaxReports;

public sealed class TaxLotMatchSnapshot
{
    private TaxLotMatchSnapshot()
    {
    }

    public TaxLotMatchSnapshot(
        Guid id,
        Guid taxCalculationRunId,
        Guid purchaseTransactionId,
        Guid saleTransactionId,
        RealizedTaxLotMatch calculation)
    {
        ArgumentNullException.ThrowIfNull(calculation);

        Id = DomainGuard.RequiredId(id, nameof(id));
        TaxCalculationRunId = DomainGuard.RequiredId(taxCalculationRunId, nameof(taxCalculationRunId));
        PurchaseTransactionId = DomainGuard.RequiredId(purchaseTransactionId, nameof(purchaseTransactionId));
        SaleTransactionId = DomainGuard.RequiredId(saleTransactionId, nameof(saleTransactionId));
        MatchedQuantity = DomainGuard.Positive(calculation.MatchedQuantity, nameof(calculation));
        PurchaseCostUsd = calculation.PurchaseCostUsd;
        PurchaseCostUah = calculation.PurchaseCostUah;
        SaleProceedsUsd = calculation.SaleProceedsUsd;
        SaleProceedsUah = calculation.SaleProceedsUah;
        PurchaseFeeUsd = calculation.PurchaseFeeUsd;
        PurchaseFeeUah = calculation.PurchaseFeeUah;
        SaleFeeUsd = calculation.SaleFeeUsd;
        SaleFeeUah = calculation.SaleFeeUah;
        GrossDifferenceUsd = calculation.GrossDifferenceUsd;
        GrossDifferenceUah = calculation.GrossDifferenceUah;
        ExpensesUsd = calculation.ExpensesUsd;
        ExpensesUah = calculation.ExpensesUah;
        ProfitUsd = calculation.ProfitUsd;
        ProfitUah = calculation.ProfitUah;
    }

    public Guid Id { get; private set; }

    public Guid TaxCalculationRunId { get; private set; }

    public Guid PurchaseTransactionId { get; private set; }

    public Guid SaleTransactionId { get; private set; }

    public decimal MatchedQuantity { get; private set; }

    public decimal PurchaseCostUsd { get; private set; }

    public decimal PurchaseCostUah { get; private set; }

    public decimal SaleProceedsUsd { get; private set; }

    public decimal SaleProceedsUah { get; private set; }

    public decimal PurchaseFeeUsd { get; private set; }

    public decimal PurchaseFeeUah { get; private set; }

    public decimal SaleFeeUsd { get; private set; }

    public decimal SaleFeeUah { get; private set; }

    public decimal GrossDifferenceUsd { get; private set; }

    public decimal GrossDifferenceUah { get; private set; }

    public decimal ExpensesUsd { get; private set; }

    public decimal ExpensesUah { get; private set; }

    public decimal ProfitUsd { get; private set; }

    public decimal ProfitUah { get; private set; }

    public TaxCalculationRun TaxCalculationRun { get; private set; } = null!;

    public InvestmentTransaction PurchaseTransaction { get; private set; } = null!;

    public InvestmentTransaction SaleTransaction { get; private set; } = null!;
}