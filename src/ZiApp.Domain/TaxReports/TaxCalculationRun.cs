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

    public string? ReportSchemaVersion { get; private set; }
    public string? InputSha256 { get; private set; }
    public string? SnapshotSha256 { get; private set; }
    public string? SnapshotJson { get; private set; }

    public void SealDraftSnapshot(string schemaVersion, string inputSha256, string snapshotSha256, string snapshotJson)
    {
        if (SnapshotJson is not null) { throw new InvalidOperationException("A saved report snapshot cannot be replaced."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        if (inputSha256.Length != 64 || snapshotSha256.Length != 64
            || !inputSha256.All(char.IsAsciiHexDigitUpper) || !snapshotSha256.All(char.IsAsciiHexDigitUpper))
        { throw new ArgumentException("Snapshot digests must be uppercase SHA-256 hex values."); }
        ReportSchemaVersion = DomainGuard.RequiredText(schemaVersion, 100, nameof(schemaVersion));
        InputSha256 = inputSha256;
        SnapshotSha256 = snapshotSha256;
        SnapshotJson = snapshotJson;
    }

    public Portfolio Portfolio { get; private set; } = null!;
}
