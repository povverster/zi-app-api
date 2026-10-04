using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/configured-tax-reports")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class ConfiguredTaxReportsController(ConfiguredTaxService service) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<ConfiguredTaxDocument>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ConfiguredTaxDocument>> Create(Guid portfolioId, CreateConfiguredTaxReportInput input, CancellationToken ct)
    {
        var result = await service.CreateAsync(portfolioId, input, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error)
            : CreatedAtAction(nameof(Get), new { portfolioId, id = result.Value.Id }, result.Value);
    }

    [HttpGet]
    [ProducesResponseType<TradingPage<ConfiguredTaxListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradingPage<ConfiguredTaxListItem>>> List(Guid portfolioId, CancellationToken ct,
        [FromQuery] int? taxYear = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.ListAsync(portfolioId, taxYear, page, pageSize, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConfiguredTaxDocument>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConfiguredTaxDocument>> Get(Guid portfolioId, Guid id, CancellationToken ct)
    {
        var result = await service.GetAsync(portfolioId, id, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/current-status")]
    [ProducesResponseType<ConfiguredTaxCurrentStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConfiguredTaxCurrentStatus>> CurrentStatus(Guid portfolioId, Guid id, CancellationToken ct)
    {
        var result = await service.CurrentStatusAsync(portfolioId, id, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(Guid portfolioId, Guid id, CancellationToken ct, [FromQuery] string format = "csv")
    {
        var result = await service.ExportAsync(portfolioId, id, format, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error)
            : File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
