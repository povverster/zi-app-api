using ZiApp.Domain.Portfolios;

namespace ZiApp.Application.Portfolios;

public sealed record PortfolioDetails(
    Guid Id,
    string Name,
    string BaseCurrencyCode,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc)
{
    public static PortfolioDetails From(Portfolio portfolio)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        return new PortfolioDetails(
            portfolio.Id,
            portfolio.Name,
            portfolio.BaseCurrencyCode,
            portfolio.IsArchived,
            portfolio.CreatedAtUtc);
    }
}

public sealed record PortfolioPage(
    IReadOnlyList<PortfolioDetails> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum PortfolioError
{
    AccountUnavailable,
    NotFound,
    NameConflict,
    InvalidName,
    InvalidPagination,
}

public sealed record PortfolioResult<T>(T? Value, PortfolioError? Error = null)
    where T : class;