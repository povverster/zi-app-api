using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Testcontainers.PostgreSql;

using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class TradeMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingTradesAndRatesAndUnsafeDowngradeIsRefused()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260830140121_AddIdentityAuthentication";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7();
        Guid portfolio = Guid.CreateVersion7();
        Guid instrument = Guid.CreateVersion7();
        Guid rate = Guid.CreateVersion7();
        Guid trade = Guid.CreateVersion7();
        DateTimeOffset time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_accounts (id, email, normalized_email, display_name, role, preferred_language, created_at_utc, is_active)
            VALUES ({owner}, 'upgrade@example.test', 'UPGRADE@EXAMPLE.TEST', 'Upgrade', 'User', 'English', {time}, true);
            INSERT INTO portfolios (id, owner_account_id, name, base_currency_code, created_at_utc, is_archived)
            VALUES ({portfolio}, {owner}, 'Existing', 'USD', {time}, false);
            INSERT INTO instruments (id, symbol, exchange_code, name, type, currency_code)
            VALUES ({instrument}, 'OLD', 'TEST', 'Existing stock', 'Stock', 'USD');
            INSERT INTO exchange_rates (id, currency_code, effective_date, rate_to_uah, source, retrieved_at_utc)
            VALUES ({rate}, 'USD', {new DateOnly(2025, 1, 1)}, 40.1234567890, 'Test', {time});
            INSERT INTO investment_transactions (id, portfolio_id, instrument_id, exchange_rate_id, side, executed_at_utc, quantity, unit_price_usd, fee_usd, broker_transaction_id)
            VALUES ({trade}, {portfolio}, {instrument}, {rate}, 'Buy', {time}, 2.123456789012, 99.5, 1.25, 'original');
            """);
        await migrator.MigrateAsync();
        var stored = await db.InvestmentTransactions.AsNoTracking().Include(item => item.ExchangeRate).SingleAsync();
        Assert.Equal(trade, stored.Id);
        Assert.Equal(trade, stored.FifoOrderId);
        Assert.False(stored.IsSuperseded);
        Assert.Null(stored.ExecutedAtOriginal);
        Assert.Equal(2.123456789012m, stored.Quantity);
        Assert.Equal(99.5m, stored.UnitPriceUsd);
        Assert.Equal(1.25m, stored.FeeUsd);
        Assert.Equal(rate, stored.ExchangeRateId);
        Assert.Equal(40.1234567890m, Assert.IsType<ZiApp.Domain.ExchangeRates.ExchangeRate>(stored.ExchangeRate).RateToUah);

        // Downgrading legacy-only data remains possible without destroying source values.
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        db.InvestmentTransactions.Add(new InvestmentTransaction(Guid.CreateVersion7(), portfolio, instrument, null,
            TradeSide.Buy, time.AddDays(1), 1m, 10m, 0m));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot downgrade", error.MessageText);
        Assert.Equal(2, await db.InvestmentTransactions.CountAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), value => value.EndsWith("_AddManualTradeEntry", StringComparison.Ordinal));
    }
}