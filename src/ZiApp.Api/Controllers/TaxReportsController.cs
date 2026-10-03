using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/tax-reports")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class TaxReportsController(TaxReportService service) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<TaxReportDocument>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TaxReportDocument>> Create(Guid portfolioId, CreateReportInput input, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(portfolioId, input, cancellationToken);
        return result.Value is null ? Failure(result) : CreatedAtAction(nameof(Get), new { portfolioId, id = result.Value.Id }, result.Value);
    }

    [HttpGet]
    [ProducesResponseType<TradingPage<ReportSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradingPage<ReportSummary>>> List(Guid portfolioId, CancellationToken cancellationToken,
        [FromQuery] int? taxYear = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.ListAsync(portfolioId, taxYear, page, pageSize, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TaxReportDocument>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaxReportDocument>> Get(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(portfolioId, id, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/current-status")]
    [ProducesResponseType<ReportCurrentStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportCurrentStatus>> CurrentStatus(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        var result = await service.CurrentStatusAsync(portfolioId, id, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(Guid portfolioId, Guid id, CancellationToken cancellationToken, [FromQuery] string format = "csv")
    {
        var result = await service.ExportAsync(portfolioId, id, format, cancellationToken);
        return result.Value is null ? Failure(result) : File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    private ActionResult Failure<T>(ReportResult<T> result) where T : class
    {
        if (result.Error == ReportError.AccountUnavailable) { return Unauthorized(); }
        if (result.Error == ReportError.NotFound) { return NotFound(); }
        var (status, title) = result.Error switch
        {
            ReportError.InvalidInput => (400, "Invalid year, pagination or export format."),
            ReportError.UnsupportedCurrency => (400, "Only USD instruments and portfolios are supported."),
            ReportError.UnresolvedRates => (409, "Resolve the listed trade rates before saving a report."),
            ReportError.TooLarge => (409, "The report exceeds the supported ledger or snapshot size."),
            ReportError.InvalidLedger => (409, "The source ledger cannot be calculated safely. No report was saved."),
            ReportError.LegacySnapshotUnavailable => (409, "This legacy run has no complete input snapshot. Generate a new draft; the legacy run is preserved."),
            ReportError.InvalidSnapshot => (409, "The saved report failed its version or integrity check."),
            _ => throw new InvalidOperationException("The report operation returned no result.")
        };
        return Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
        {
            ["code"] = result.Error.ToString(),
            ["rateBlockers"] = result.RateBlockers
        });
    }
}
