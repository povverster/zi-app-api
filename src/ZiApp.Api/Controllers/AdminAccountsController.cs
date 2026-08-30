using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Accounts;
using ZiApp.Api.Security;
using ZiApp.Application.Accounts;
using ZiApp.Application.Security;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
[Route("api/admin/accounts")]
public sealed class AdminAccountsController(
    IAccountProvisioningService provisioningService) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryHeader]
    public async Task<ActionResult<AccountResponse>> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        AccountProvisioningResult result = await provisioningService.ProvisionAsync(
            new ProvisionAccountCommand(
                request.Email,
                request.DisplayName,
                request.Password,
                request.Role,
                request.PreferredLanguage),
            cancellationToken);

        if (!result.Succeeded || result.Account is null)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(nameof(request.Email), error);
            }

            return ValidationProblem(ModelState);
        }

        AccountResponse response = AccountResponse.From(result.Account);
        return Created($"/api/admin/accounts/{response.Id}", response);
    }
}