using Microsoft.AspNetCore.Mvc;

using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

public abstract class TradingControllerBase : ControllerBase
{
    protected ActionResult Failure(TradingError? error)
    {
        if (error == TradingError.AccountUnavailable)
        {
            return Unauthorized();
        }

        if (error == TradingError.NotFound)
        {
            return NotFound();
        }

        var (status, title) = error switch
        {
            TradingError.InvalidInput => (400, "Invalid input. Check precision, timestamp, text, and pagination limits."),
            TradingError.UnsupportedCurrency => (400, "Only USD instruments and portfolios are supported."),
            TradingError.Duplicate => (409, "This instrument or active broker transaction ID already exists."),
            TradingError.Archived => (409, "Restore the portfolio before entering or correcting trades."),
            TradingError.Oversold => (409, "This change would leave a sale without enough earlier purchased units."),
            TradingError.AlreadyCorrected => (409, "This trade was already corrected. Use its current replacement."),
            _ => throw new InvalidOperationException("The trading operation returned no result."),
        };
        return Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }
}