using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Testcontainers.PostgreSql;

using ZiApp.Application.ExchangeRates;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class NbuMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradePreservesLegacyInputsAndDowngradeProtectsNewProvenance(bool resolve)
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260920125916_AddManualTradeEntry";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7();
        Guid portfolio = Guid.CreateVersion7();
        Guid instrument = Guid.CreateVersion7();
        Guid linkedTrade = Guid.CreateVersion7();
        Guid pendingTrade = Guid.CreateVersion7();
        Guid oldRate = Guid.CreateVersion7();
        DateOnly day = new(2025, 1, 5);
        DateTimeOffset time = new(2025, 1, 5, 23, 30, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_accounts (id, email, normalized_email, display_name, role, preferred_language, created_at_utc, is_active)
            VALUES ({owner}, 'nbu-upgrade@example.test', 'NBU-UPGRADE@EXAMPLE.TEST', 'Upgrade', 'User', 'English', {time}, true);
            INSERT INTO portfolios (id, owner_account_id, name, base_currency_code, created_at_utc, is_archived)
            VALUES ({portfolio}, {owner}, 'Existing', 'USD', {time}, false);
            INSERT INTO instruments (id, symbol, exchange_code, name, type, currency_code)
            VALUES ({instrument}, 'OLD', 'TEST', 'Existing stock', 'Stock', 'USD');
            INSERT INTO exchange_rates (id, currency_code, effective_date, rate_to_uah, source, retrieved_at_utc)
            VALUES ({oldRate}, 'USD', {day}, 40.1234567890, 'NBU', {time});
            INSERT INTO investment_transactions (id, fifo_order_id, portfolio_id, instrument_id, exchange_rate_id, side, executed_at_utc, executed_at_original, quantity, unit_price_usd, fee_usd, is_superseded)
            VALUES ({linkedTrade}, {linkedTrade}, {portfolio}, {instrument}, {oldRate}, 'Buy', {time}, NULL, 2.123456789012, 99.5, 1.25, false),
                   ({pendingTrade}, {pendingTrade}, {portfolio}, {instrument}, NULL, 'Buy', {time}, '2025-01-05T23:30:00Z', 1, 100, 1, false);
            """);
        await migrator.MigrateAsync();
        var linked = await db.InvestmentTransactions.AsNoTracking().Include(item => item.ExchangeRate).SingleAsync(item => item.Id == linkedTrade);
        Assert.Equal(2.123456789012m, linked.Quantity);
        Assert.Equal(oldRate, linked.ExchangeRateId);
        Assert.Null(linked.ExchangeRatePolicy);
        Assert.Null(linked.ExecutedAtOriginal);
        var legacyRate = Assert.IsType<ExchangeRate>(linked.ExchangeRate);
        Assert.Equal(40.1234567890m, legacyRate.RateToUah);
        Assert.Null(legacyRate.RawResponseJson);
        Assert.Equal("NBU", legacyRate.Source);

        var pending = await db.InvestmentTransactions.SingleAsync(item => item.Id == pendingTrade);
        Assert.Null(pending.ExchangeRateId);
        Assert.Equal("2025-01-05T23:30:00Z", pending.ExecutedAtOriginal);
        // No new provenance exists yet: a downgrade and re-upgrade are safe.
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();

        using var handler = new NbuTestHandler(_ => Task.FromResult(NbuRateClientTests.Response(NbuRateClientTests.Payload(day))));
        using var http = new HttpClient(handler);
        var client = new ZiApp.Infrastructure.ExchangeRates.NbuRateClient(http, TimeProvider.System);
        var rate = Assert.IsType<ExchangeRate>((await client.FetchAsync(day, CancellationToken.None)).Value);
        db.ExchangeRates.Add(rate);
        if (resolve)
        {
            pending = await db.InvestmentTransactions.SingleAsync(item => item.Id == pendingTrade);
            pending.ResolveExchangeRate(rate, day, TradeRatePolicy.Version, owner, DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot downgrade while NBU provenance", error.MessageText);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), value => value.EndsWith("_AddNbuExchangeRates", StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.ExchangeRates.CountAsync());
        Assert.Equal(NbuRateClientTests.Payload(day), (await db.ExchangeRates.SingleAsync(item => item.Id == rate.Id)).RawResponseJson);
        pending = await db.InvestmentTransactions.SingleAsync(item => item.Id == pendingTrade);
        Assert.Equal(resolve ? rate.Id : (Guid?)null, pending.ExchangeRateId);
    }
}