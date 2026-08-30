using ZiApp.Domain.Accounts;
using ZiApp.Domain.Common;

namespace ZiApp.Domain.Portfolios;

public sealed class Portfolio
{
    private Portfolio()
    {
    }

    public Portfolio(
        Guid id,
        Guid ownerAccountId,
        string name,
        string baseCurrencyCode,
        DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        OwnerAccountId = DomainGuard.RequiredId(ownerAccountId, nameof(ownerAccountId));
        Name = DomainGuard.RequiredText(name, 200, nameof(name));
        BaseCurrencyCode = NormalizeCurrencyCode(baseCurrencyCode);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerAccountId { get; private set; }

    public string Name { get; private set; } = null!;

    public string BaseCurrencyCode { get; private set; } = null!;

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public UserAccount OwnerAccount { get; private set; } = null!;

    public void Archive()
    {
        IsArchived = true;
    }

    private static string NormalizeCurrencyCode(string value)
    {
        string currencyCode = DomainGuard.RequiredText(value, 3, nameof(value)).ToUpperInvariant();
        if (currencyCode.Length != 3)
        {
            throw new ArgumentException("Currency code must contain exactly three characters.", nameof(value));
        }

        return currencyCode;
    }
}