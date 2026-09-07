using Microsoft.EntityFrameworkCore;

using Npgsql;

using ZiApp.Application.Portfolios;
using ZiApp.Domain.Portfolios;

namespace ZiApp.Infrastructure.Persistence;

public sealed class PortfolioRepository(ApplicationDbContext dbContext) : IPortfolioRepository
{
    public async Task<PortfolioPage> ListAsync(
        Guid ownerAccountId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Portfolio> query = dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.OwnerAccountId == ownerAccountId);

        if (!includeArchived)
        {
            query = query.Where(portfolio => !portfolio.IsArchived);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        List<PortfolioDetails> items = await query
            .OrderBy(portfolio => portfolio.CreatedAtUtc)
            .ThenBy(portfolio => portfolio.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(portfolio => new PortfolioDetails(
                portfolio.Id,
                portfolio.Name,
                portfolio.BaseCurrencyCode,
                portfolio.IsArchived,
                portfolio.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PortfolioPage(items, page, pageSize, totalCount);
    }

    public Task<Portfolio?> FindAsync(
        Guid ownerAccountId,
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Portfolios.SingleOrDefaultAsync(
            portfolio => portfolio.Id == portfolioId && portfolio.OwnerAccountId == ownerAccountId,
            cancellationToken);
    }

    public async Task<bool> AddAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        dbContext.Portfolios.Add(portfolio);
        if (!await SaveAsync(portfolio, cancellationToken))
        {
            return false;
        }

        // Return persisted timestamp precision consistently on create and later reads.
        await dbContext.Entry(portfolio).ReloadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_portfolios_owner_account_id_name",
            })
        {
            dbContext.Entry(portfolio).State = EntityState.Detached;
            return false;
        }
    }
}