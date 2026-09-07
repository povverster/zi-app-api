using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ZiApp.Api.Portfolios;
using ZiApp.Api.Security;
using ZiApp.Application.Portfolios;

namespace ZiApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public sealed class PortfoliosController(IPortfolioService portfolioService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PortfolioPage>> List(
        [FromQuery] ListPortfoliosRequest request,
        CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioPage> result = await portfolioService.ListAsync(
            request.Page, request.PageSize, request.IncludeArchived, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDetails>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioDetails> result = await portfolioService.GetAsync(id, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType<PortfolioDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortfolioDetails>> Create(
        PortfolioNameRequest request,
        CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioDetails> result = await portfolioService.CreateAsync(
            request.Name, cancellationToken);

        return result.Value is null
            ? Failure(result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortfolioDetails>> Rename(
        Guid id,
        PortfolioNameRequest request,
        CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioDetails> result = await portfolioService.RenameAsync(
            id, request.Name, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/archive")]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDetails>> Archive(Guid id, CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioDetails> result = await portfolioService.SetArchivedAsync(
            id, true, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/restore")]
    [ValidateAntiforgeryHeader]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDetails>> Restore(Guid id, CancellationToken cancellationToken)
    {
        PortfolioResult<PortfolioDetails> result = await portfolioService.SetArchivedAsync(
            id, false, cancellationToken);
        return result.Value is null ? Failure(result.Error) : Ok(result.Value);
    }

    private ActionResult Failure(PortfolioError? error)
    {
        return error switch
        {
            PortfolioError.AccountUnavailable => Unauthorized(),
            PortfolioError.NotFound => NotFound(),
            PortfolioError.NameConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A portfolio with this name already exists in your account."),
            PortfolioError.InvalidName => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Portfolio name must contain between 1 and 200 characters."),
            PortfolioError.InvalidPagination => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The requested page or page size is invalid."),
            _ => throw new InvalidOperationException("The portfolio operation returned no result."),
        };
    }
}