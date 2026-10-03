using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Testcontainers.PostgreSql;

using ZiApp.Application.Reports;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class ReportMigrationTests
{
    [Theory]
    [InlineData("snapshot")]
    [InlineData("precision")]
    [InlineData("range")]
    public async Task UpgradePreservesLegacyRunsAndDowngradeCannotEraseSnapshotsOrPrecision(string mode)
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261002125349_AddSplitManagement";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7(), portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        Guid buy = Guid.CreateVersion7(), sell = Guid.CreateVersion7(), run = Guid.CreateVersion7(), match = Guid.CreateVersion7();
        DateTimeOffset time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_accounts (id, email, normalized_email, display_name, role, preferred_language, created_at_utc, is_active)
            VALUES ({owner}, 'report-upgrade@example.test', 'REPORT-UPGRADE@EXAMPLE.TEST', 'Upgrade', 'User', 'English', {time}, true);
            INSERT INTO portfolios (id, owner_account_id, name, base_currency_code, created_at_utc, is_archived)
            VALUES ({portfolio}, {owner}, 'Existing', 'USD', {time}, false);
            INSERT INTO instruments (id, symbol, exchange_code, name, type, currency_code)
            VALUES ({instrument}, 'OLD', 'TEST', 'Existing', 'Stock', 'USD');
            INSERT INTO investment_transactions (id, fifo_order_id, portfolio_id, instrument_id, side, executed_at_utc, quantity, unit_price_usd, fee_usd, is_superseded)
            VALUES ({buy}, {buy}, {portfolio}, {instrument}, 'Buy', {time}, 1, 100, 1, false),
                   ({sell}, {sell}, {portfolio}, {instrument}, 'Sell', {time.AddDays(1)}, 1, 110, 1, false);
            INSERT INTO tax_calculation_runs (id, portfolio_id, tax_year, calculation_version, created_at_utc)
            VALUES ({run}, {portfolio}, 2025, 'fifo-uah-v1', {time});
            INSERT INTO tax_lot_match_snapshots (id, tax_calculation_run_id, purchase_transaction_id, sale_transaction_id,
                matched_quantity, purchase_cost_usd, purchase_cost_uah, sale_proceeds_usd, sale_proceeds_uah,
                purchase_fee_usd, purchase_fee_uah, sale_fee_usd, sale_fee_uah,
                gross_difference_usd, gross_difference_uah, expenses_usd, expenses_uah, profit_usd, profit_uah)
            VALUES ({match}, {run}, {buy}, {sell}, 1, 100.123456789012, 4000, 110, 4510, 1, 40, 1, 41,
                9.876543210988, 510, 2, 81, 7.876543210988, 429);
            """);
        await migrator.MigrateAsync();
        Assert.Null((await db.TaxCalculationRuns.AsNoTracking().SingleAsync(r => r.Id == run)).SnapshotJson);
        Assert.Equal(100.123456789012m, (await db.TaxLotMatchSnapshots.AsNoTracking().SingleAsync(m => m.Id == match)).PurchaseCostUsd);
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        if (mode == "snapshot")
        {
            var stored = await db.TaxCalculationRuns.SingleAsync(r => r.Id == run);
            stored.SealDraftSnapshot(ReportSnapshot.SchemaVersion, ReportSnapshot.Hash("{}"), ReportSnapshot.Hash("{}"), "{}");
            await db.SaveChangesAsync();
        }
        else
        {
            decimal value = mode == "precision" ? 0.3333333333333333333333333333m : 10000000000000000m;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tax_lot_match_snapshots SET profit_usd = {value} WHERE id = {match}");
            Assert.Equal(value, (await db.TaxLotMatchSnapshots.AsNoTracking().SingleAsync(m => m.Id == match)).ProfitUsd);
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains(mode == "snapshot" ? "saved draft report snapshots" : "saved calculation precision", error.MessageText);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_AddDraftTaxReports", StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(100.123456789012m, (await db.TaxLotMatchSnapshots.SingleAsync(m => m.Id == match)).PurchaseCostUsd);
        Assert.Equal(2, await db.InvestmentTransactions.CountAsync());
    }
}
