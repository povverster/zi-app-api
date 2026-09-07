using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using ZiApp.Application.Security;
using ZiApp.Infrastructure.Identity;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.Api.Accounts;

public sealed class CurrentAccount(
    IHttpContextAccessor httpContextAccessor,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext) : ICurrentAccount
{
    public async Task<Guid?> GetActiveAccountIdAsync(CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(userManager.GetUserId(principal), out Guid identityUserId))
        {
            return null;
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == identityUserId && user.Account.IsActive)
            .Select(user => (Guid?)user.AccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}