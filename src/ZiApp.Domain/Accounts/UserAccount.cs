using ZiApp.Domain.Common;

namespace ZiApp.Domain.Accounts;

public enum AccountRole
{
    User = 1,
    SuperAdmin = 2,
}

public enum SupportedLanguage
{
    English = 1,
    Ukrainian = 2,
    Russian = 3,
}

public sealed class UserAccount
{
    private UserAccount()
    {
    }

    public UserAccount(
        Guid id,
        string email,
        string displayName,
        AccountRole role,
        SupportedLanguage preferredLanguage,
        DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        Email = DomainGuard.RequiredText(email, 320, nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
        DisplayName = DomainGuard.RequiredText(displayName, 200, nameof(displayName));
        Role = DomainGuard.DefinedEnum(role, nameof(role));
        PreferredLanguage = DomainGuard.DefinedEnum(preferredLanguage, nameof(preferredLanguage));
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string NormalizedEmail { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public AccountRole Role { get; private set; }

    public SupportedLanguage PreferredLanguage { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }
}