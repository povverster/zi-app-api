using ZiApp.Domain.Common;

namespace ZiApp.Domain.TaxReports;

public sealed class ConfiguredTaxReport
{
    private ConfiguredTaxReport() { }

    public ConfiguredTaxReport(Guid id, Guid sourceReportId, Guid settingsId, int taxYear,
        DateTimeOffset createdAtUtc, string schemaVersion, string snapshotJson, string snapshotSha256)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        SourceReportId = DomainGuard.RequiredId(sourceReportId, nameof(sourceReportId));
        SettingsId = DomainGuard.RequiredId(settingsId, nameof(settingsId));
        ArgumentOutOfRangeException.ThrowIfLessThan(taxYear, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(taxYear, 9998);
        TaxYear = taxYear;
        CreatedAtUtc = createdAtUtc;
        SchemaVersion = DomainGuard.RequiredText(schemaVersion, 100, nameof(schemaVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        if (snapshotSha256.Length != 64 || !snapshotSha256.All(char.IsAsciiHexDigitUpper))
        { throw new ArgumentException("A SHA-256 digest is required.", nameof(snapshotSha256)); }
        SnapshotJson = snapshotJson;
        SnapshotSha256 = snapshotSha256;
    }

    public Guid Id { get; private set; }
    public Guid SourceReportId { get; private set; }
    public Guid SettingsId { get; private set; }
    public int TaxYear { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string SchemaVersion { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public string SnapshotSha256 { get; private set; } = null!;
    public TaxCalculationRun SourceReport { get; private set; } = null!;
    public TaxSettingsRevision Settings { get; private set; } = null!;
}
