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

public sealed class AnnualSummaryMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingReportsAndDowngradeProtectsSavedAnnualDrafts()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261002140835_AddDraftTaxReports";
        await migrator.MigrateAsync(previous);
        Guid owner = Guid.CreateVersion7(), portfolio = Guid.CreateVersion7(), report = Guid.CreateVersion7();
        DateTimeOffset captured = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var run = new TaxCalculationRun(report, portfolio, 2025, "existing-policy", captured);
        run.SealDraftSnapshot(ReportSnapshot.SchemaVersion, ReportSnapshot.Hash("{}"), ReportSnapshot.Hash("{}"), "{}");
        db.AddRange(new UserAccount(owner, "upgrade@example.test", "Owner", AccountRole.User, SupportedLanguage.English, captured),
            new Portfolio(portfolio, owner, "Existing portfolio", "USD", captured), run);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("{}", (await db.TaxCalculationRuns.SingleAsync(r => r.Id == report)).SnapshotJson);
        Assert.Equal(portfolio, (await db.Portfolios.SingleAsync()).Id);
        Assert.Empty(await db.AnnualPreparationDrafts.ToListAsync());
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        var input = new CreateAnnualSummaryInput(2025, [], "Unknown", [], "Unknown", [], true, true);
        var built = AnnualSummaryBuilder.Build(owner, input, [new(portfolio, "Existing portfolio", false)], [], captured);
        db.AnnualPreparationDrafts.Add(built.Draft);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("saved annual preparation drafts", error.MessageText);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_AddAnnualPreparationDrafts", StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(built.Draft.SnapshotJson, (await db.AnnualPreparationDrafts.SingleAsync()).SnapshotJson);
        Assert.Equal("{}", (await db.TaxCalculationRuns.SingleAsync()).SnapshotJson);
    }
}
