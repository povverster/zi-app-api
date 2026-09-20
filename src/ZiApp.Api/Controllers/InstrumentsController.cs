using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/instruments")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public sealed class InstrumentsController(InstrumentService service) : TradingControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TradingPage<InstrumentDetails>>> List(CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.ListAsync(search, page, pageSize, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InstrumentDetails>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<InstrumentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InstrumentDetails>> Create(InstrumentInput input, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(input, cancellationToken);
        return result.Value is null ? Failure(result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }
}