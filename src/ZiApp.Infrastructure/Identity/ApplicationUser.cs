using Microsoft.AspNetCore.Identity;

using ZiApp.Domain.Accounts;

namespace ZiApp.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    private ApplicationUser()
    {
    }

    public ApplicationUser(Guid id, Guid accountId, string email)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("ID cannot be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = id;
        AccountId = accountId;
        UserName = email.Trim();
        Email = email.Trim();
    }

    public Guid AccountId { get; private set; }

    public UserAccount Account { get; private set; } = null!;
}