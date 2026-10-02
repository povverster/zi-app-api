using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Testcontainers.PostgreSql;

using ZiApp.Domain.Instruments;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class SplitMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradePreservesLegacySplitsAndDowngradeProtectsAuditHistory(bool correctLegacy)
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260920191527_AddNbuExchangeRates";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        DateTimeOffset time = new(2025, 1, 5, 12, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_accounts (id, email, normalized_email, display_name, role, preferred_language, created_at_utc, is_active)
            VALUES ({owner}, 'split-upgrade@example.test', 'SPLIT-UPGRADE@EXAMPLE.TEST', 'Upgrade', 'SuperAdmin', 'English', {time}, true);
            INSERT INTO instruments (id, symbol, exchange_code, name, type, currency_code)
            VALUES ({instrument}, 'OLD', 'TEST', 'Existing stock', 'Stock', 'USD');
            INSERT INTO stock_splits (id, instrument_id, effective_at_utc, numerator, denominator)
            VALUES ({first}, {instrument}, {time}, 2.123456789012, 1),
                   ({second}, {instrument}, {time.AddDays(1)}, 1, 3);
            """);
        await migrator.MigrateAsync();
        var legacy = await db.StockSplits.AsNoTracking().SingleAsync(s => s.Id == first);
        Assert.Equal(first, legacy.FifoOrderId);
        Assert.Equal(time, legacy.EffectiveAtUtc);
        Assert.Equal(2.123456789012m, legacy.Numerator);
        Assert.Equal(1m, legacy.Denominator);
        Assert.False(legacy.IsSuperseded);
        Assert.Null(legacy.RecordedByAccountId);
        Assert.Null(legacy.RecordedAtUtc);
        Assert.Null(legacy.EffectiveAtOriginal);
        Assert.Null(legacy.SourceReference);
        Assert.Equal(second, (await db.StockSplits.AsNoTracking().SingleAsync(s => s.Id == second)).FifoOrderId);

        // Legacy-only data can safely downgrade and re-upgrade.
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        Guid replacementId = Guid.CreateVersion7();
        if (correctLegacy)
        {
            (await db.StockSplits.SingleAsync(s => s.Id == first)).Supersede();
            await db.SaveChangesAsync();
        }
        db.StockSplits.Add(new StockSplit(replacementId, instrument, time, 2m, 1m,
            correctLegacy ? first : null, "2025-01-05T14:00:00+02:00", owner, DateTimeOffset.UtcNow,
            "Official corporate action", correctLegacy ? first : null, correctLegacy ? "Correct legacy ratio" : null));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot downgrade while split provenance", error.MessageText);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_AddSplitManagement", StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.StockSplits.CountAsync());
        var replacement = await db.StockSplits.SingleAsync(s => s.Id == replacementId);
        Assert.Equal(owner, replacement.RecordedByAccountId);
        Assert.Equal("Official corporate action", replacement.SourceReference);
        legacy = await db.StockSplits.SingleAsync(s => s.Id == first);
        Assert.Equal(2.123456789012m, legacy.Numerator);
        Assert.Equal(correctLegacy, legacy.IsSuperseded);
        Assert.Equal(correctLegacy ? first : (Guid?)null, replacement.PreviousSplitId);
    }
}