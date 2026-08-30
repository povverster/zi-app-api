using ZiApp.Domain.Common;
using ZiApp.Domain.Portfolios;

namespace ZiApp.Domain.TaxReports;

public sealed class TaxCalculationRun
{
    private TaxCalculationRun()
    {
    }

    public TaxCalculationRun(
        Guid id,
        Guid portfolioId,
        int taxYear,
        string calculationVersion,
        DateTimeOffset createdAtUtc)
    {
        if (taxYear is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(taxYear), "Tax year is outside the supported range.");
        }

        Id = DomainGuard.RequiredId(id, nameof(id));
        PortfolioId = DomainGuard.RequiredId(portfolioId, nameof(portfolioId));
        TaxYear = taxYear;
        CalculationVersion = DomainGuard.RequiredText(calculationVersion, 100, nameof(calculationVersion));
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PortfolioId { get; private set; }

    public int TaxYear { get; private set; }

    public string CalculationVersion { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Portfolio Portfolio { get; private set; } = null!;
}