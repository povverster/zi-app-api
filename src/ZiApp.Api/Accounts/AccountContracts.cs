using System.ComponentModel.DataAnnotations;

using ZiApp.Application.Accounts;
using ZiApp.Domain.Accounts;

namespace ZiApp.Api.Accounts;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string Password { get; init; } = string.Empty;
}

public sealed class CreateAccountRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    public AccountRole Role { get; init; }

    public SupportedLanguage PreferredLanguage { get; init; }
}

public sealed record CsrfTokenResponse(string Token);

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    AccountRole Role,
    SupportedLanguage PreferredLanguage,
    bool IsActive)
{
    public static AccountResponse From(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountResponse(
            account.Id,
            account.Email,
            account.DisplayName,
            account.Role,
            account.PreferredLanguage,
            account.IsActive);
    }

    public static AccountResponse From(ProvisionedAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountResponse(
            account.Id,
            account.Email,
            account.DisplayName,
            account.Role,
            account.PreferredLanguage,
            account.IsActive);
    }
}