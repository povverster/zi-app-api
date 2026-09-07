using System.ComponentModel.DataAnnotations;

namespace ZiApp.Api.Portfolios;

public sealed class PortfolioNameRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;
}

public sealed class ListPortfoliosRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public bool IncludeArchived { get; init; }
}