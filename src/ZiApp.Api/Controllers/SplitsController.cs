using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Ledger;
using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/instruments/{instrumentId:guid}/splits")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class SplitsController(SplitService service) : LedgerControllerBase
{
    [HttpGet]
    [ProducesResponseType<TradingPage<SplitDetails>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradingPage<SplitDetails>>> List(Guid instrumentId, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool includeSuperseded = false)
    {
        var result = await service.ListAsync(instrumentId, page, pageSize, includeSuperseded, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SplitDetails>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SplitDetails>> Get(Guid instrumentId, Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(instrumentId, id, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<SplitDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SplitDetails>> Create(Guid instrumentId, SplitInput input, CancellationToken cancellationToken) =>
        CreatedSplit(instrumentId, await service.RecordAsync(instrumentId, input, null, null, cancellationToken));

    [HttpPost("{id:guid}/corrections")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<SplitDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SplitDetails>> Correct(Guid instrumentId, Guid id, CorrectSplitInput input, CancellationToken cancellationToken) =>
        CreatedSplit(instrumentId, await service.RecordAsync(instrumentId, input.Replacement, id, input.Reason, cancellationToken));

    private ActionResult<SplitDetails> CreatedSplit(Guid instrumentId, LedgerResult<SplitDetails> result) =>
        result.Value is null ? Failure(result.Error) : CreatedAtAction(nameof(Get), new { instrumentId, id = result.Value.Id }, result.Value);
}