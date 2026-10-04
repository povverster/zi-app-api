using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Security;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tax-settings/{year:int}")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class TaxSettingsController(ConfiguredTaxService service) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<TaxSettingsDetails>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TaxSettingsDetails>> Save(int year, SaveTaxSettingsInput input, CancellationToken ct)
    {
        var result = await service.SaveSettingsAsync(year, input, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet]
    [ProducesResponseType<TaxSettingsDetails>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaxSettingsDetails>> Get(int year, CancellationToken ct)
    {
        var result = await service.LatestSettingsAsync(year, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("history")]
    [ProducesResponseType<TradingPage<TaxSettingsDetails>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradingPage<TaxSettingsDetails>>> History(int year, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await service.SettingsHistoryAsync(year, page, pageSize, ct);
        return result.Value is null ? ConfiguredTaxFailure.Result(this, result.Error) : Ok(result.Value);
    }
}

internal static class ConfiguredTaxFailure
{
    internal static ActionResult Result(ControllerBase controller, ConfiguredTaxError? error)
    {
        if (error == ConfiguredTaxError.AccountUnavailable) { return controller.Unauthorized(); }
        if (error == ConfiguredTaxError.NotFound) { return controller.NotFound(); }
        var (status, title) = error switch
        {
            ConfiguredTaxError.InvalidInput => (400, "Invalid year, percentage, report/settings ID, pagination or format."),
            ConfiguredTaxError.YearMismatch => (400, "Select settings for the source report's year."),
            ConfiguredTaxError.InvalidSource => (409, "The source report failed integrity or policy validation."),
            ConfiguredTaxError.LegacySnapshotUnavailable => (409, "Generate a new source draft; this legacy run has no complete snapshot."),
            ConfiguredTaxError.InvalidSnapshot => (409, "The configured report failed its version or integrity check."),
            ConfiguredTaxError.TooLarge => (409, "The source or configured snapshot exceeds the size limit."),
            ConfiguredTaxError.ArithmeticNotRepresentable => (409, "The tax amounts exceed the supported monetary range."),
            _ => throw new InvalidOperationException("The configured tax operation returned no result.")
        };
        return controller.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }
}
