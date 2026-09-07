using ZiApp.Application.Security;
using ZiApp.Domain.Portfolios;

namespace ZiApp.Application.Portfolios;

public sealed class PortfolioService(
    ICurrentAccount currentAccount,
    IPortfolioRepository repository) : IPortfolioService
{
    public async Task<PortfolioResult<PortfolioPage>> ListAsync(
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        Guid? accountId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (accountId is null)
        {
            return new(null, PortfolioError.AccountUnavailable);
        }

        if (page < 1 || pageSize < 1 || pageSize > 100
            || ((long)page - 1) * pageSize > int.MaxValue)
        {
            return new(null, PortfolioError.InvalidPagination);
        }

        PortfolioPage result = await repository.ListAsync(
            accountId.Value, page, pageSize, includeArchived, cancellationToken);
        return new(result);
    }

    public async Task<PortfolioResult<PortfolioDetails>> GetAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        Guid? accountId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (accountId is null)
        {
            return new(null, PortfolioError.AccountUnavailable);
        }

        Portfolio? portfolio = await repository.FindAsync(accountId.Value, portfolioId, cancellationToken);
        return portfolio is null
            ? new(null, PortfolioError.NotFound)
            : new(PortfolioDetails.From(portfolio));
    }

    public async Task<PortfolioResult<PortfolioDetails>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        Guid? accountId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (accountId is null)
        {
            return new(null, PortfolioError.AccountUnavailable);
        }

        if (!IsValidName(name))
        {
            return new(null, PortfolioError.InvalidName);
        }

        var portfolio = new Portfolio(
            Guid.CreateVersion7(), accountId.Value, name, "USD", DateTimeOffset.UtcNow);

        return await repository.AddAsync(portfolio, cancellationToken)
            ? new(PortfolioDetails.From(portfolio))
            : new(null, PortfolioError.NameConflict);
    }

    public async Task<PortfolioResult<PortfolioDetails>> RenameAsync(
        Guid portfolioId,
        string name,
        CancellationToken cancellationToken = default)
    {
        Guid? accountId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (accountId is null)
        {
            return new(null, PortfolioError.AccountUnavailable);
        }

        Portfolio? portfolio = await repository.FindAsync(accountId.Value, portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return new(null, PortfolioError.NotFound);
        }

        if (!IsValidName(name))
        {
            return new(null, PortfolioError.InvalidName);
        }

        portfolio.Rename(name);
        return await SaveAsync(portfolio, cancellationToken);
    }

    public async Task<PortfolioResult<PortfolioDetails>> SetArchivedAsync(
        Guid portfolioId,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        Guid? accountId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (accountId is null)
        {
            return new(null, PortfolioError.AccountUnavailable);
        }

        Portfolio? portfolio = await repository.FindAsync(accountId.Value, portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return new(null, PortfolioError.NotFound);
        }

        if (isArchived)
        {
            portfolio.Archive();
        }
        else
        {
            portfolio.Restore();
        }

        return await SaveAsync(portfolio, cancellationToken);
    }

    private async Task<PortfolioResult<PortfolioDetails>> SaveAsync(
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        return await repository.SaveAsync(portfolio, cancellationToken)
            ? new(PortfolioDetails.From(portfolio))
            : new(null, PortfolioError.NameConflict);
    }

    private static bool IsValidName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 200;
    }
}