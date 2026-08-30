using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class InvestmentLedgerPersistenceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task InvestmentLedgerMigrationIsApplied()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrations = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(migrations, migration =>
            migration.EndsWith("_InitialInvestmentLedger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AccountPortfolioAndTradeRoundTripThroughPostgreSql()
    {
        Guid accountId = Guid.NewGuid();
        Guid portfolioId = Guid.NewGuid();
        Guid instrumentId = Guid.NewGuid();
        Guid rateId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var account = new UserAccount(
            accountId,
            $"investor-{accountId:N}@example.test",
            "Integration Investor",
            AccountRole.User,
            SupportedLanguage.English,
            now);
        var portfolio = new Portfolio(portfolioId, accountId, "Main", "USD", now);
        var instrument = new Instrument(
            instrumentId,
            $"T{instrumentId:N}"[..12],
            "NASDAQ",
            "Integration ETF",
            InstrumentType.Etf,
            "USD");
        var rate = new ExchangeRate(
            rateId,
            "USD",
            DateOnly.FromDateTime(now.UtcDateTime),
            42.1549m,
            $"NBU-{rateId:N}",
            now);
        var transaction = new InvestmentTransaction(
            transactionId,
            portfolioId,
            instrumentId,
            rateId,
            TradeSide.Buy,
            now,
            2.5m,
            89.52m,
            2.12m,
            $"broker-{transactionId:N}");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.AddRange(account, portfolio, instrument, rate, transaction);
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedTransaction = await dbContext.InvestmentTransactions
                .AsNoTracking()
                .Include(item => item.Portfolio)
                .ThenInclude(item => item.OwnerAccount)
                .Include(item => item.Instrument)
                .Include(item => item.ExchangeRate)
                .SingleAsync(item => item.Id == transactionId);

            Assert.Equal("Integration Investor", storedTransaction.Portfolio.OwnerAccount.DisplayName);
            Assert.Equal(InstrumentType.Etf, storedTransaction.Instrument.Type);
            Assert.Equal(2.5m, storedTransaction.Quantity);
            Assert.Equal(42.1549m, storedTransaction.ExchangeRate.RateToUah);
        }
    }
}