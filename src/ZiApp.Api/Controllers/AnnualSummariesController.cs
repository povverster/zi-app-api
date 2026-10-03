using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/annual-summaries")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class AnnualSummariesController(AnnualSummaryService service) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryHeader]
    [RequestSizeLimit(256 * 1024)]
    [ProducesResponseType<AnnualSummaryDocument>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AnnualSummaryDocument>> Create(CreateAnnualSummaryInput input, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(input, cancellationToken);
        return result.Value is null ? Failure(result) : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet]
    [ProducesResponseType<TradingPage<AnnualSummaryListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradingPage<AnnualSummaryListItem>>> List(CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AnnualSummaryDocument>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnualSummaryDocument>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/current-status")]
    [ProducesResponseType<AnnualSummaryCurrentStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnualSummaryCurrentStatus>> CurrentStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.CurrentStatusAsync(id, cancellationToken);
        return result.Value is null ? Failure(result) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.ExportAsync(id, cancellationToken);
        return result.Value is null ? Failure(result) : File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    private ActionResult Failure<T>(AnnualResult<T> result) where T : class
    {
        if (result.Error == AnnualSummaryError.AccountUnavailable) { return Unauthorized(); }
        if (result.Error == AnnualSummaryError.NotFound) { return NotFound(); }
        return Problem(statusCode: result.Error == AnnualSummaryError.InvalidInput ? 400 : 409,
            title: "The annual preparation draft could not be processed.",
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.ToString() });
    }
}
