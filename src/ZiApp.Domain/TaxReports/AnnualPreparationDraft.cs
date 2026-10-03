using ZiApp.Domain.Accounts;
using ZiApp.Domain.Common;

namespace ZiApp.Domain.TaxReports;

public sealed class AnnualPreparationDraft
{
    private AnnualPreparationDraft() { }

    public AnnualPreparationDraft(Guid id, Guid ownerAccountId, int taxYear, DateTimeOffset createdAtUtc,
        string schemaVersion, string snapshotSha256, string snapshotJson)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        OwnerAccountId = DomainGuard.RequiredId(ownerAccountId, nameof(ownerAccountId));
        ArgumentOutOfRangeException.ThrowIfNotEqual(taxYear, 2025);
        TaxYear = taxYear;
        CreatedAtUtc = createdAtUtc;
        SchemaVersion = DomainGuard.RequiredText(schemaVersion, 100, nameof(schemaVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        if (snapshotSha256.Length != 64 || !snapshotSha256.All(char.IsAsciiHexDigitUpper))
        { throw new ArgumentException("A SHA-256 digest is required.", nameof(snapshotSha256)); }
        SnapshotSha256 = snapshotSha256;
        SnapshotJson = snapshotJson;
    }

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public int TaxYear { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string SchemaVersion { get; private set; } = null!;
    public string SnapshotSha256 { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public UserAccount OwnerAccount { get; private set; } = null!;
}
