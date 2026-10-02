using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Application.Ledger;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class HoldingsController(HoldingsService service) : LedgerControllerBase
{
    [HttpGet]
    [ProducesResponseType<HoldingsDetails>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HoldingsDetails>> Get(Guid portfolioId, CancellationToken cancellationToken, [FromQuery] string? asOf = null)
    {
        var result = await service.GetAsync(portfolioId, asOf, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }
}