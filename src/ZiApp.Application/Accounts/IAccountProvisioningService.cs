using ZiApp.Domain.Accounts;

namespace ZiApp.Application.Accounts;

public sealed record ProvisionAccountCommand(
    string Email,
    string DisplayName,
    string Password,
    AccountRole Role,
    SupportedLanguage PreferredLanguage);

public sealed record ProvisionedAccount(
    Guid Id,
    string Email,
    string DisplayName,
    AccountRole Role,
    SupportedLanguage PreferredLanguage,
    bool IsActive);

public sealed record AccountProvisioningResult(
    ProvisionedAccount? Account,
    IReadOnlyCollection<string> Errors)
{
    public bool Succeeded => Account is not null;
}

public interface IAccountProvisioningService
{
    Task<AccountProvisioningResult> ProvisionAsync(
        ProvisionAccountCommand command,
        CancellationToken cancellationToken = default);
}