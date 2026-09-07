using ZiApp.Domain.Portfolios;

namespace ZiApp.UnitTests.InvestmentLedger;

public sealed class PortfolioTests
{
    [Fact]
    public void RenameAndRepeatedArchiveRestorePreservePortfolioIdentityAndCurrency()
    {
        Guid id = Guid.CreateVersion7();
        Guid ownerId = Guid.CreateVersion7();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var portfolio = new Portfolio(id, ownerId, "Original", "USD", createdAt);

        portfolio.Archive();
        portfolio.Archive();
        portfolio.Rename("  Renamed  ");
        Assert.True(portfolio.IsArchived);
        Assert.Equal("Renamed", portfolio.Name);
        portfolio.Restore();
        portfolio.Restore();
        Assert.False(portfolio.IsArchived);
        Assert.Equal(id, portfolio.Id);
        Assert.Equal(ownerId, portfolio.OwnerAccountId);
        Assert.Equal("USD", portfolio.BaseCurrencyCode);
        Assert.Equal(createdAt, portfolio.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameRejectsEmptyNameWithoutChangingPortfolio(string? name)
    {
        var portfolio = new Portfolio(Guid.CreateVersion7(), Guid.CreateVersion7(), "Original", "USD", DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => portfolio.Rename(name!));
        Assert.Equal("Original", portfolio.Name);
    }

    [Fact]
    public void RenameRejectsOverlongNameWithoutChangingPortfolio()
    {
        var portfolio = new Portfolio(Guid.CreateVersion7(), Guid.CreateVersion7(), "Original", "USD", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => portfolio.Rename(new string('x', 201)));
        Assert.Equal("Original", portfolio.Name);
    }
}