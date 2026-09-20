using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.ExchangeRates;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/exchange-rates")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
public sealed class ExchangeRatesController(ExchangeRateService rates, TradeRateService trades) : ControllerBase
{
    [HttpGet("usd/{date}")]
    [ProducesResponseType<ExchangeRateDetails>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExchangeRateDetails>> Get(DateOnly date, CancellationToken cancellationToken)
    {
        var result = await rates.GetAsync(date, false, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost("usd/{date}/fetch")]
    [ProducesResponseType<ExchangeRateDetails>(StatusCodes.Status200OK)]
    [ValidateAntiforgeryHeader]
    public async Task<ActionResult<ExchangeRateDetails>> Fetch(DateOnly date, CancellationToken cancellationToken)
    {
        var result = await rates.GetAsync(date, true, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("/api/portfolios/{portfolioId:guid}/trades/{tradeId:guid}/exchange-rate")]
    [ProducesResponseType<TradeRateDetails>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradeRateDetails>> Trade(Guid portfolioId, Guid tradeId, CancellationToken cancellationToken)
    {
        var result = await trades.GetAsync(portfolioId, tradeId, false, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost("/api/portfolios/{portfolioId:guid}/trades/{tradeId:guid}/exchange-rate/resolve")]
    [ProducesResponseType<TradeRateDetails>(StatusCodes.Status200OK)]
    [ValidateAntiforgeryHeader]
    public async Task<ActionResult<TradeRateDetails>> Resolve(Guid portfolioId, Guid tradeId, CancellationToken cancellationToken)
    {
        var result = await trades.GetAsync(portfolioId, tradeId, true, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    private ActionResult Failure(RateError? error)
    {
        if (error == RateError.AccountUnavailable) { return Unauthorized(); }
        if (error == RateError.NotFound) { return NotFound(); }
        var (status, title) = error switch
        {
            RateError.InvalidDate => (400, "The date must be in the UAH period and not in the future."),
            RateError.RateMissing => (404, "NBU returned no USD rate for the exact requested date. No fallback was used."),
            RateError.UpstreamUnavailable => (503, "NBU is temporarily unavailable. Retry later; no trade rate was changed."),
            RateError.InvalidResponse => (502, "NBU returned a response that could not be validated. No trade rate was changed."),
            RateError.Archived => (409, "Restore the portfolio before resolving its rates."),
            RateError.Superseded => (409, "Resolve the current replacement, not a superseded trade."),
            RateError.LegacyRateLocked => (409, "The existing rate link is preserved. An audited correction is required to change it."),
            RateError.InvalidOriginalTimestamp => (409, "The stored broker timestamp is invalid or inconsistent; correct the trade first."),
            RateError.RateConflict => (409, "The rate or trade state conflicts with this resolution. Existing records were preserved."),
            _ => throw new InvalidOperationException("The exchange-rate operation returned no result."),
        };
        return Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }
}