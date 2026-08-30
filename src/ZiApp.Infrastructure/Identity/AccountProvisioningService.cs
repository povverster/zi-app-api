using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Accounts;
using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.Infrastructure.Identity;

public sealed class AccountProvisioningService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager) : IAccountProvisioningService
{
    public async Task<AccountProvisioningResult> ProvisionAsync(
        ProvisionAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        List<string> validationErrors = Validate(command);
        if (validationErrors.Count > 0)
        {
            return new AccountProvisioningResult(null, validationErrors);
        }

        string email = command.Email.Trim();
        string normalizedEmail = userManager.NormalizeEmail(email) ?? email.ToUpperInvariant();
        bool accountExists = await dbContext.UserAccounts
            .AnyAsync(account => account.NormalizedEmail == normalizedEmail, cancellationToken);

        if (accountExists)
        {
            return Failure("An account with this email already exists.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var account = new UserAccount(
                Guid.CreateVersion7(),
                email,
                command.DisplayName.Trim(),
                command.Role,
                command.PreferredLanguage,
                DateTimeOffset.UtcNow);

            dbContext.UserAccounts.Add(account);
            await dbContext.SaveChangesAsync(cancellationToken);

            var identityUser = new ApplicationUser(Guid.CreateVersion7(), account.Id, email);
            IdentityResult createResult = await userManager.CreateAsync(identityUser, command.Password);

            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return Failure(createResult);
            }

            IdentityResult roleResult = await userManager.AddToRoleAsync(
                identityUser,
                command.Role.ToString());

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return Failure(roleResult);
            }

            await transaction.CommitAsync(cancellationToken);

            return new AccountProvisioningResult(
                new ProvisionedAccount(
                    account.Id,
                    account.Email,
                    account.DisplayName,
                    account.Role,
                    account.PreferredLanguage,
                    account.IsActive),
                []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static List<string> Validate(ProvisionAccountCommand command)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            errors.Add("Email is required.");
        }
        else if (command.Email.Trim().Length > 256)
        {
            errors.Add("Email cannot be longer than 256 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            errors.Add("Display name is required.");
        }
        else if (command.DisplayName.Trim().Length > 200)
        {
            errors.Add("Display name cannot be longer than 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.Password))
        {
            errors.Add("Password is required.");
        }
        else if (command.Password.Length > 256)
        {
            errors.Add("Password cannot be longer than 256 characters.");
        }

        if (!Enum.IsDefined(command.Role))
        {
            errors.Add("Account role is invalid.");
        }

        if (!Enum.IsDefined(command.PreferredLanguage))
        {
            errors.Add("Preferred language is invalid.");
        }

        return errors;
    }

    private static AccountProvisioningResult Failure(IdentityResult result)
    {
        string[] errors = result.Errors
            .Select(error => error.Description)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new AccountProvisioningResult(null, errors);
    }

    private static AccountProvisioningResult Failure(string error)
    {
        return new AccountProvisioningResult(null, [error]);
    }
}