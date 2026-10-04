using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;
using Testcontainers.PostgreSql;

using ZiApp.Application.Reports;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.TaxReports;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class ConfiguredTaxMigrationTests
{
    [Fact]
    public async Task UpgradePreservesDataAndDowngradeProtectsSettingsAndReports()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261003152645_AddAnnualPreparationDrafts";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7(), portfolio = Guid.CreateVersion7(), sourceId = Guid.CreateVersion7();
        DateTimeOffset captured = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var source = new TaxCalculationRun(sourceId, portfolio, 2025, "prior-policy", captured);
        source.SealDraftSnapshot(ReportSnapshot.SchemaVersion, ReportSnapshot.Hash("{}"), ReportSnapshot.Hash("{}"), "{}");
        db.AddRange(new UserAccount(owner, "configured-migration@example.test", "Owner", AccountRole.User, SupportedLanguage.English, captured),
            new Portfolio(portfolio, owner, "Existing", "USD", captured), source,
            new AnnualPreparationDraft(Guid.CreateVersion7(), owner, 2025, captured, AnnualSummarySnapshot.SchemaVersion,
                ReportSnapshot.Hash("{}"), "{}"));
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("{}", (await db.TaxCalculationRuns.SingleAsync()).SnapshotJson);
        Assert.Equal("{}", (await db.AnnualPreparationDrafts.SingleAsync()).SnapshotJson);
        Assert.Empty(await db.ConfiguredTaxReports.ToListAsync());
        Assert.Empty(await db.TaxSettingsRevisions.ToListAsync());
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        var rates = new TaxSettingsRevision(Guid.CreateVersion7(), owner, 2025, 18m, 5m, 9m, captured);
        db.TaxSettingsRevisions.Add(rates);
        await db.SaveChangesAsync();
        var settingsError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("saved configured tax reports or settings", settingsError.MessageText);
        db.ConfiguredTaxReports.Add(new(Guid.CreateVersion7(), sourceId, rates.Id, 2025, captured,
            ConfiguredTaxSnapshot.SchemaVersion, "{}", ReportSnapshot.Hash("{}")));
        await db.SaveChangesAsync();
        var reportError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("saved configured tax reports or settings", reportError.MessageText);
        db.ChangeTracker.Clear();
        Assert.Single(await db.ConfiguredTaxReports.ToListAsync());
        Assert.Single(await db.TaxSettingsRevisions.ToListAsync());
        Assert.Equal("{}", (await db.TaxCalculationRuns.SingleAsync()).SnapshotJson);
        Assert.Contains("20261004144649_AddConfiguredTaxReports", await db.Database.GetAppliedMigrationsAsync());
    }
}
