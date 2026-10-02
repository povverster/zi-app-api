using Microsoft.AspNetCore.Mvc;

using ZiApp.Application.Ledger;

namespace ZiApp.Api.Controllers;

public abstract class LedgerControllerBase : ControllerBase
{
    protected ActionResult Failure(LedgerError? error)
    {
        if (error == LedgerError.AccountUnavailable) { return Unauthorized(); }
        if (error == LedgerError.NotFound) { return NotFound(); }
        var (status, title) = error switch
        {
            LedgerError.InvalidInput => (400, "Invalid precision, timestamp, source, reason, or pagination."),
            LedgerError.UnsupportedCurrency => (400, "Only USD instruments and portfolios are supported."),
            LedgerError.Duplicate => (409, "An active split already exists at that effective instant. Use an audited correction."),
            LedgerError.AlreadyCorrected => (409, "This split was already corrected. Use its current replacement."),
            LedgerError.InvalidLedger => (409, "The ledger cannot be calculated safely, or this split would invalidate recorded sales. No source records were changed."),
            _ => throw new InvalidOperationException("The ledger operation returned no result."),
        };
        return Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }
}