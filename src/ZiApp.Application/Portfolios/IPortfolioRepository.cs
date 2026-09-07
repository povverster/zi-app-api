using ZiApp.Domain.Portfolios;

namespace ZiApp.Application.Portfolios;

public interface IPortfolioRepository
{
    Task<PortfolioPage> ListAsync(
        Guid ownerAccountId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<Portfolio?> FindAsync(
        Guid ownerAccountId,
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    // False means the owner's portfolio-name uniqueness constraint was violated.
    Task<bool> AddAsync(Portfolio portfolio, CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default);
}