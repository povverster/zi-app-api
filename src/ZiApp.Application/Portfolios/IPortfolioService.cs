namespace ZiApp.Application.Portfolios;

public interface IPortfolioService
{
    Task<PortfolioResult<PortfolioPage>> ListAsync(
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<PortfolioResult<PortfolioDetails>> GetAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task<PortfolioResult<PortfolioDetails>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<PortfolioResult<PortfolioDetails>> RenameAsync(
        Guid portfolioId,
        string name,
        CancellationToken cancellationToken = default);

    Task<PortfolioResult<PortfolioDetails>> SetArchivedAsync(
        Guid portfolioId,
        bool isArchived,
        CancellationToken cancellationToken = default);
}