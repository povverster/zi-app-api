using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/trades")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class TradesController(TradeService service) : TradingControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TradingPage<TradeDetails>>> List(Guid portfolioId, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool includeSuperseded = false)
    {
        var result = await service.ListAsync(portfolioId, page, pageSize, includeSuperseded, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TradeDetails>> Get(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(portfolioId, id, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("corrections")]
    public async Task<ActionResult<TradingPage<CorrectionDetails>>> Corrections(Guid portfolioId,
        CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.ListCorrectionsAsync(portfolioId, page, pageSize, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<TradeDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TradeDetails>> Create(Guid portfolioId, TradeInput input, CancellationToken cancellationToken)
    {
        var result = await service.RecordAsync(portfolioId, input, null, null, cancellationToken);
        return CreatedTrade(portfolioId, result);
    }

    [HttpPost("{id:guid}/corrections")]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<TradeDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TradeDetails>> Correct(Guid portfolioId, Guid id,
        CorrectTradeInput input, CancellationToken cancellationToken)
    {
        var result = await service.RecordAsync(portfolioId, input.Replacement, id, input.Reason, cancellationToken);
        return CreatedTrade(portfolioId, result);
    }

    private ActionResult<TradeDetails> CreatedTrade(Guid portfolioId, TradingResult<TradeDetails> result) =>
        result.Value is null ? Failure(result.Error)
            : CreatedAtAction(nameof(Get), new { portfolioId, id = result.Value.Id }, result.Value);
}