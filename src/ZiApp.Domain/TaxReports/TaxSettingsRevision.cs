using ZiApp.Domain.Accounts;
using ZiApp.Domain.Common;
using ZiApp.Domain.Tax;

namespace ZiApp.Domain.TaxReports;

public sealed class TaxSettingsRevision
{
    private TaxSettingsRevision() { }

    public TaxSettingsRevision(Guid id, Guid ownerAccountId, int taxYear, decimal investmentIncomePercent,
        decimal militaryPercent, decimal dividendIncomePercent, DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        OwnerAccountId = DomainGuard.RequiredId(ownerAccountId, nameof(ownerAccountId));
        ArgumentOutOfRangeException.ThrowIfLessThan(taxYear, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(taxYear, 9998);
        ConfiguredTaxCalculator.ValidateRate(investmentIncomePercent);
        ConfiguredTaxCalculator.ValidateRate(militaryPercent);
        ConfiguredTaxCalculator.ValidateRate(dividendIncomePercent);
        TaxYear = taxYear;
        InvestmentIncomePercent = investmentIncomePercent;
        MilitaryPercent = militaryPercent;
        DividendIncomePercent = dividendIncomePercent;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public int TaxYear { get; private set; }
    public decimal InvestmentIncomePercent { get; private set; }
    public decimal MilitaryPercent { get; private set; }
    public decimal DividendIncomePercent { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public UserAccount OwnerAccount { get; private set; } = null!;
}
