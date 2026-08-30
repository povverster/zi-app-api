using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using ZiApp.Api.Accounts;
using ZiApp.Api.Security;
using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Identity;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "A security token could not be created.");
        }

        return Ok(new CsrfTokenResponse(tokens.RequestToken));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiforgeryHeader]
    public async Task<ActionResult<AccountResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? identityUser = await userManager.FindByEmailAsync(request.Email.Trim());

        if (identityUser is null)
        {
            return Unauthorized();
        }

        UserAccount? account = await dbContext.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == identityUser.AccountId, cancellationToken);

        if (account is null || !account.IsActive)
        {
            return Unauthorized();
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.PasswordSignInAsync(
            identityUser,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        return Ok(AccountResponse.From(account));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> Me(CancellationToken cancellationToken)
    {
        ApplicationUser? identityUser = await userManager.GetUserAsync(User);
        if (identityUser is null)
        {
            return Unauthorized();
        }

        UserAccount? account = await dbContext.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == identityUser.AccountId, cancellationToken);

        return account is not null && account.IsActive
            ? Ok(AccountResponse.From(account))
            : Unauthorized();
    }

    [Authorize]
    [HttpPost("logout")]
    [ValidateAntiforgeryHeader]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }
}