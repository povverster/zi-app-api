using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Application.Accounts;
using ZiApp.Application.ExchangeRates;
using ZiApp.Application.Ledger;
using ZiApp.Application.Reports;
using ZiApp.Application.Trading;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class TaxReportTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000002")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000002/current-status")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000002/export")]
    public async Task AnonymousRequestsAreRejected(string method, string suffix)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/portfolios/00000000-0000-0000-0000-000000000001/tax-reports" + suffix);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnnualReportReplaysPriorYearsAndPersistsEveryCalculatedDigit()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        var document = await CreateAsync(owner, seed);
        Assert.Equal(7, document.Id.Version);
        Assert.Equal(2025, document.TaxYear);
        Assert.Equal("Draft", document.Status);
        Assert.False(document.IsTaxReady);
        Assert.Equal("broker-calendar-year-v1", document.YearPolicyVersion);
        var match = Assert.Single(document.Matches);
        Assert.Equal(seed.BuyId, match.Values.PurchaseTradeId);
        Assert.Equal(seed.SaleId, match.Values.SaleTradeId);
        Assert.Equal("1", match.Values.Quantity);
        Assert.InRange(Parse(document.Totals.ProfitUsd), 15.333333m, 15.333334m);
        Assert.Equal(3, document.Inputs.Trades.Count); // Includes the prior year's consuming sale.
        Assert.Equal(seed.SplitId, Assert.Single(document.Inputs.Splits).Id);
        Assert.Equal(3, document.Inputs.Rates.Count);
        Assert.All(document.Inputs.Rates, r => Assert.NotNull(r.RawResponseJson));
        Assert.Equal(ReportSnapshot.InputHash(document.Inputs), document.InputSha256);
        decimal expectedCost = Parse(match.Values.PurchaseCostUsd);
        Assert.NotEqual(decimal.Round(expectedCost, 12), expectedCost);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TaxLotMatchSnapshots.SingleAsync(m => m.TaxCalculationRunId == document.Id);
        Assert.Equal(expectedCost, row.PurchaseCostUsd);
        Assert.Equal(Parse(match.Values.PurchaseFeeUsd), row.PurchaseFeeUsd);
        Assert.Equal(Parse(document.Totals.ProfitUah), row.ProfitUah);
        var run = await db.TaxCalculationRuns.SingleAsync(r => r.Id == document.Id);
        Assert.Equal(ReportSnapshot.Hash(run.SnapshotJson!), run.SnapshotSha256);
        using var exported = await owner.Client.GetAsync(Path(seed) + $"/{document.Id}/export?format=json");
        Assert.Equal(run.SnapshotJson, await exported.Content.ReadAsStringAsync());
        Assert.Equal("application/json", exported.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", exported.Content.Headers.ContentDisposition!.DispositionType);
        using var read = await owner.Client.GetAsync(Path(seed) + $"/{document.Id}");
        var restored = await ReadAsync<TaxReportDocument>(read);
        Assert.Equal(ReportSnapshot.Serialize(document), ReportSnapshot.Serialize(restored));
        Assert.Contains("no-store", read.Headers.CacheControl!.ToString());
        using var current = await owner.Client.GetAsync(Path(seed) + $"/{document.Id}/current-status");
        Assert.True((await ReadAsync<ReportCurrentStatus>(current)).MatchesCurrentInputs);
    }

    [Fact]
    public async Task CorrectionsCreateNewDraftsWithoutChangingSavedExports()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        var original = await CreateAsync(owner, seed);
        using var before = await owner.Client.GetAsync(Path(seed) + $"/{original.Id}/export?format=json");
        string savedJson = await before.Content.ReadAsStringAsync();
        using var correction = await owner.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.BuyId}/corrections",
            new CorrectTradeInput(new(seed.InstrumentId, TradeSide.Buy, "2024-01-01T12:00:00Z", "1", "90", "1"), "Fix purchase price"));
        var replacement = await ReadAsync<TradeDetails>(correction, HttpStatusCode.Created);
        using var status = await owner.Client.GetAsync(Path(seed) + $"/{original.Id}/current-status");
        Assert.Equal("InputsChanged", (await ReadAsync<ReportCurrentStatus>(status)).Status);
        using var blocked = await owner.PostAsync(Path(seed), new CreateReportInput(2025));
        await ErrorAsync(blocked, "UnresolvedRates");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var oldBuy = await db.InvestmentTransactions.Include(t => t.ExchangeRate).SingleAsync(t => t.Id == seed.BuyId);
            (await db.InvestmentTransactions.SingleAsync(t => t.Id == replacement.Id))
                .ResolveExchangeRate(oldBuy.ExchangeRate!, new(2024, 1, 1), TradeRatePolicy.Version, owner.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var revised = await CreateAsync(owner, seed);
        Assert.NotEqual(original.Id, revised.Id);
        Assert.NotEqual(original.InputSha256, revised.InputSha256);
        Assert.NotEqual(original.Totals.ProfitUsd, revised.Totals.ProfitUsd);
        Assert.Equal(replacement.Id, Assert.Single(revised.Matches).Values.PurchaseTradeId);
        using var after = await owner.Client.GetAsync(Path(seed) + $"/{original.Id}/export?format=json");
        Assert.Equal(savedJson, await after.Content.ReadAsStringAsync());
        using var list = await owner.Client.GetAsync(Path(seed) + "?taxYear=2025&pageSize=1");
        var page = await ReadAsync<TradingPage<ReportSummary>>(list);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(revised.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task SplitCorrectionMarksInputsChangedButKeepsTheOriginalSplitSnapshot()
    {
        using var owner = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(owner);
        var original = await CreateAsync(owner, seed);
        using var changed = await owner.PostAsync($"/api/instruments/{seed.InstrumentId}/splits/{seed.SplitId}/corrections",
            new CorrectSplitInput(new("2024-01-02T12:00:00Z", "2", "1", "Corrected notice"), "Ratio correction"));
        await ReadAsync<SplitDetails>(changed, HttpStatusCode.Created);
        using var status = await owner.Client.GetAsync(Path(seed) + $"/{original.Id}/current-status");
        Assert.False((await ReadAsync<ReportCurrentStatus>(status)).MatchesCurrentInputs);
        using var old = await owner.Client.GetAsync(Path(seed) + $"/{original.Id}");
        Assert.Equal("3", Assert.Single((await ReadAsync<TaxReportDocument>(old)).Inputs.Splits).Numerator);
        var revised = await CreateAsync(owner, seed);
        Assert.Equal("2", Assert.Single(revised.Inputs.Splits).Numerator);
        Assert.NotEqual(original.Totals.ProfitUsd, revised.Totals.ProfitUsd);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task ReportsArePrivateEvenFromOtherAdministrators(AccountRole role)
    {
        using var owner = await ActorAsync();
        using var stranger = await ActorAsync(role);
        var seed = await SeedAsync(owner);
        var report = await CreateAsync(owner, seed);
        foreach (string suffix in new[] { "", $"/{report.Id}", $"/{report.Id}/export?format=csv", $"/{report.Id}/export?format=json", $"/{report.Id}/current-status" })
        {
            using var response = await stranger.Client.GetAsync(Path(seed) + suffix);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var create = await stranger.PostAsync(Path(seed), new CreateReportInput(2025));
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingAndLegacyRateLinksBlockSavingWithoutPartialReports(bool legacy)
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner, verified: false, legacy: legacy);
        using var response = await owner.PostAsync(Path(seed), new CreateReportInput(2025));
        await ErrorAsync(response, "UnresolvedRates");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("rateBlockers").GetArrayLength());
        Assert.Equal(legacy ? "LinkedUnverified" : "Pending", json.RootElement.GetProperty("rateBlockers")[0].GetProperty("status").GetString());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.TaxCalculationRuns.AnyAsync(r => r.PortfolioId == seed.PortfolioId));
        Assert.All(await db.InvestmentTransactions.Where(t => t.PortfolioId == seed.PortfolioId).ToListAsync(),
            trade => Assert.Null(trade.ExchangeRatePolicy));
    }

    [Fact]
    public async Task LaterPendingPurchasesDoNotBlockAnEarlierAnnualReport()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.InvestmentTransactions.Add(new(Guid.CreateVersion7(), seed.PortfolioId, seed.InstrumentId, null,
                TradeSide.Buy, new(2025, 2, 1, 12, 0, 0, TimeSpan.Zero), 1m, 10m, 0m, executedAtOriginal: "2025-02-01T12:00:00Z"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(3, (await CreateAsync(owner, seed)).Inputs.Trades.Count);
    }

    [Theory]
    [InlineData("2024-12-31T23:30:00-05:00", 2024, 2025)]
    [InlineData("2025-01-01T00:30:00+02:00", 2025, 2024)]
    public async Task ReportYearUsesBrokerDateNotUtcYear(string saleTime, int brokerYear, int utcYear)
    {
        using var owner = await ActorAsync();
        var seed = await BoundarySeedAsync(owner, saleTime);
        var included = await CreateAsync(owner, seed, brokerYear);
        Assert.Single(included.Matches);
        Assert.Equal(brokerYear, included.Inputs.Trades.Single(t => t.Id == seed.SaleId).BrokerDate.Year);
        Assert.Equal(utcYear, included.Inputs.Trades.Single(t => t.Id == seed.SaleId).ExecutedAtUtc.Year);
        var excluded = await CreateAsync(owner, seed, utcYear);
        Assert.Empty(excluded.Matches);
        Assert.Equal("0", excluded.Totals.ProfitUah);
    }

    [Fact]
    public async Task EmptyArchivedPortfoliosHaveZeroDraftsAndDisabledOwnersAreRejected()
    {
        using var owner = await ActorAsync();
        Guid portfolio = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = new Portfolio(portfolio, owner.Id, "Empty archived", "USD", DateTimeOffset.UtcNow);
            item.Archive();
            db.Add(item);
            await db.SaveChangesAsync();
        }
        var seed = new Seed(portfolio, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        var report = await CreateAsync(owner, seed);
        Assert.Empty(report.Matches);
        Assert.Empty(report.Inputs.Trades);
        Assert.Null(report.ReplayThroughUtc);
        Assert.Equal("0", report.Totals.ProfitUsd);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(a => a.Id == owner.Id)).Deactivate();
            await db.SaveChangesAsync();
        }
        foreach (string suffix in new[] { "", $"/{report.Id}", $"/{report.Id}/export", $"/{report.Id}/current-status" })
        {
            using var read = await owner.Client.GetAsync(Path(seed) + suffix);
            Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        }
        using var create = await owner.PostAsync(Path(seed), new CreateReportInput(2025));
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task ValidationCsrfAndLegacyReadBehaviorAreExplicit()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        using var csrf = await owner.PostAsync(Path(seed), new CreateReportInput(2025), "");
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        foreach (int year in new[] { 0, 1999, 9999 })
        {
            using var invalid = await owner.PostAsync(Path(seed), new CreateReportInput(year));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var paging = await owner.Client.GetAsync(Path(seed) + "?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, paging.StatusCode);
        using var format = await owner.Client.GetAsync(Path(seed) + $"/{Guid.CreateVersion7()}/export?format=pdf");
        Assert.Equal(HttpStatusCode.BadRequest, format.StatusCode);
        Guid legacy = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TaxCalculationRuns.Add(new(legacy, seed.PortfolioId, 2025, "fifo-uah-v1", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        using var list = await owner.Client.GetAsync(Path(seed));
        Assert.Equal("LegacySnapshotUnavailable", Assert.Single((await ReadAsync<TradingPage<ReportSummary>>(list)).Items).Status);
        foreach (string suffix in new[] { "", "/export?format=json", "/current-status" })
        {
            using var read = await owner.Client.GetAsync(Path(seed) + $"/{legacy}" + suffix);
            await ErrorAsync(read, "LegacySnapshotUnavailable");
        }
    }

    [Fact]
    public async Task ChangedSnapshotBytesFailIntegrityChecks()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        var report = await CreateAsync(owner, seed);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tax_calculation_runs SET snapshot_json = '{{}}' WHERE id = {report.Id}");
        }
        using var read = await owner.Client.GetAsync(Path(seed) + $"/{report.Id}");
        await ErrorAsync(read, "InvalidSnapshot");
        using var export = await owner.Client.GetAsync(Path(seed) + $"/{report.Id}/export");
        await ErrorAsync(export, "InvalidSnapshot");
    }

    [Fact]
    public async Task ConcurrentCorrectionDoesNotMixVersionsInsideOneReport()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner);
        var expected = await CreateAsync(owner, seed);
        await using var scope = fixture.Services.CreateAsyncScope();
        string connection = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
        var pause = new PortfolioReadPause();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).AddInterceptors(pause).Options;
        await using var isolated = new ApplicationDbContext(options);
        var repository = new TaxReportRepository(isolated, TimeProvider.System);
        var creating = repository.CreateAsync(owner.Id, seed.PortfolioId, 2025, CancellationToken.None);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var correction = await owner.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.BuyId}/corrections",
                new CorrectTradeInput(new(seed.InstrumentId, TradeSide.Buy, "2024-01-01T12:00:00Z", "1", "90", "1"), "Concurrent change"));
            await ReadAsync<TradeDetails>(correction, HttpStatusCode.Created);
        }
        finally { pause.Release.TrySetResult(); }
        var result = await creating.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(result.Value);
        Assert.Equal(expected.InputSha256, result.Value.InputSha256);
        Assert.Equal(expected.Totals, result.Value.Totals);
        using var current = await owner.Client.GetAsync(Path(seed) + $"/{result.Value.Id}/current-status");
        Assert.Equal("InputsChanged", (await ReadAsync<ReportCurrentStatus>(current)).Status);
    }

    [Fact]
    public async Task SwaggerAndCsvExposeDraftStatusAndDecimalStrings()
    {
        using var owner = await ActorAsync();
        var seed = await SeedAsync(owner, name: "=1+1");
        var report = await CreateAsync(owner, seed);
        using var csv = await owner.Client.GetAsync(Path(seed) + $"/{report.Id}/export?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        string text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("\"'=1+1\"", text);
        Assert.Contains("\"Draft\",\"false\"", text);
        Assert.Contains("\"" + report.Matches[0].Values.PurchaseCostUsd + "\"", text);
        Assert.Contains("\"Total\"", text);
        using var swagger = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        using var json = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/api/portfolios/{portfolioId}/tax-reports/{id}/export", out _));
        Assert.Equal("string", json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("MatchDetails")
            .GetProperty("properties").GetProperty("purchaseCostUsd").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SavedAnnualReportReconcilesTheIbitSpreadsheetExample()
    {
        using var owner = await ActorAsync();
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(new Portfolio(portfolio, owner.Id, "IBIT report", "USD", DateTimeOffset.UtcNow),
                new Instrument(instrument, "IBIT", instrument.ToString("N")[..20], "IBIT regression", InstrumentType.Etf, "USD"));
            await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, "2025-10-30T14:30:01Z", 4m, 61.53m, 2.08m, 42.0115m);
            await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, "2025-11-03T17:01:14Z", 2m, 59.95m, 2.04m, 41.8924m);
            await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, "2025-11-20T20:40:45Z", 4m, 48.97m, 2.08m, 42.0948m);
            await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Sell, "2026-02-18T15:30:00Z", 10m, 38.01m, 2.20m, 43.2577m);
            await db.SaveChangesAsync();
        }
        var document = await CreateAsync(owner, new(portfolio, instrument, Guid.Empty, Guid.Empty, Guid.Empty), 2026);
        Assert.Equal(3, document.Matches.Count);
        Assert.Collection(document.Matches,
            match => Assert.Equal("4", match.Values.Quantity),
            match => Assert.Equal("2", match.Values.Quantity),
            match => Assert.Equal("4", match.Values.Quantity));
        Assert.InRange(Parse(document.Totals.ProfitUsd), -190.200001m, -190.199999m);
        Assert.InRange(Parse(document.Totals.ProfitUah), -7521.615335m, -7521.615333m);
        using var csv = await owner.Client.GetAsync($"/api/portfolios/{portfolio}/tax-reports/{document.Id}/export");
        Assert.Contains(document.Totals.ProfitUah, await csv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VerifiedLegacyTradesKeepTheirStoredCalendarDateForReporting()
    {
        using var owner = await ActorAsync();
        var seed = await BoundarySeedAsync(owner, "2025-01-01T23:30:00Z");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE investment_transactions SET executed_at_original = NULL, exchange_rate_policy = {TradeRatePolicy.LegacyVersion}
                WHERE portfolio_id = {seed.PortfolioId}
                """);
        }
        var document = await CreateAsync(owner, seed);
        Assert.Single(document.Matches);
        Assert.All(document.Inputs.Trades, t =>
        {
            Assert.Null(t.ExecutedAtOriginal);
            Assert.Equal(TradeRatePolicy.LegacyVersion, t.ExchangeRatePolicy);
            Assert.Equal(DateOnly.FromDateTime(t.ExecutedAtUtc.DateTime), t.BrokerDate);
        });
    }

    private sealed class PortfolioReadPause : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM portfolios", StringComparison.Ordinal) && Entered.TrySetResult())
            { await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            return result;
        }
    }

    private async Task<Seed> SeedAsync(Actor owner, bool verified = true, bool legacy = false, string? name = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7(), split = Guid.CreateVersion7();
        db.AddRange(new Portfolio(portfolio, owner.Id, name ?? portfolio.ToString(), "USD", DateTimeOffset.UtcNow),
            new Instrument(instrument, instrument.ToString("N")[..20], "TEST", "Report stock", InstrumentType.Stock, "USD"),
            new StockSplit(split, instrument, new(2024, 1, 2, 12, 0, 0, TimeSpan.Zero), 3m, 1m,
                effectiveAtOriginal: "2024-01-02T12:00:00Z", recordedByAccountId: owner.Id,
                recordedAtUtc: DateTimeOffset.UtcNow, sourceReference: "Split notice"));
        var buy = await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, "2024-01-01T12:00:00Z", 1m, 100m, 1m, 40m, verified, legacy);
        await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Sell, "2024-01-03T12:00:00Z", 1m, 40m, 1m, 41m, verified, legacy);
        var sale = await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Sell, "2025-01-03T12:00:00Z", 1m, 50m, 1m, 42m, verified, legacy);
        await db.SaveChangesAsync();
        return new(portfolio, instrument, buy.Id, sale.Id, split);
    }

    private async Task<Seed> BoundarySeedAsync(Actor owner, string saleTime)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        db.AddRange(new Portfolio(portfolio, owner.Id, portfolio.ToString(), "USD", DateTimeOffset.UtcNow),
            new Instrument(instrument, instrument.ToString("N")[..20], "TEST", "Boundary stock", InstrumentType.Stock, "USD"));
        var buy = await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, "2024-01-01T12:00:00Z", 1m, 100m, 1m, 40m);
        var sale = await AddTradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Sell, saleTime, 1m, 110m, 1m, 42m);
        await db.SaveChangesAsync();
        return new(portfolio, instrument, buy.Id, sale.Id, Guid.Empty);
    }

    private static async Task<InvestmentTransaction> AddTradeAsync(ApplicationDbContext db, Guid owner, Guid portfolio, Guid instrument,
        TradeSide side, string original, decimal quantity, decimal price, decimal fee, decimal fx, bool verified = true, bool legacy = false)
    {
        var instant = DateTimeOffset.Parse(original, CultureInfo.InvariantCulture);
        DateOnly date = DateOnly.FromDateTime(instant.DateTime);
        ExchangeRate? rate = null;
        if (verified || legacy)
        {
            rate = verified ? await db.ExchangeRates.SingleOrDefaultAsync(r => r.Source == NbuRateSource.Key && r.EffectiveDate == date) : null;
            if (rate is null)
            {
                rate = new(Guid.CreateVersion7(), "USD", date, fx, verified ? NbuRateSource.Key : Guid.CreateVersion7().ToString(),
                    DateTimeOffset.UtcNow, verified ? date : null, verified ? "https://bank.gov.ua/test" : null,
                    verified ? ReportSnapshot.Hash("[]") : null, verified ? "[]" : null);
                db.ExchangeRates.Add(rate);
            }
        }
        var trade = new InvestmentTransaction(Guid.CreateVersion7(), portfolio, instrument, legacy ? rate!.Id : null,
            side, instant.ToUniversalTime(), quantity, price, fee, executedAtOriginal: original);
        if (verified) { trade.ResolveExchangeRate(rate!, date, TradeRatePolicy.Version, owner, DateTimeOffset.UtcNow); }
        db.InvestmentTransactions.Add(trade);
        return trade;
    }

    private static async Task<TaxReportDocument> CreateAsync(Actor actor, Seed seed, int year = 2025)
    {
        using var response = await actor.PostAsync(Path(seed), new CreateReportInput(year));
        var result = await ReadAsync<TaxReportDocument>(response, HttpStatusCode.Created);
        Assert.EndsWith("/" + result.Id, response.Headers.Location!.ToString());
        return result;
    }
    private static string Path(Seed seed) => $"/api/portfolios/{seed.PortfolioId}/tax-reports";
    private sealed record Seed(Guid PortfolioId, Guid InstrumentId, Guid BuyId, Guid SaleId, Guid SplitId);
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private async Task<Actor> ActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Reports!Testing123";
        string email = $"reports-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var provision = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
            var result = await provision.ProvisionAsync(new(email, "Reporter", password, role, SupportedLanguage.English));
            Assert.True(result.Succeeded);
            id = Assert.IsType<ProvisionedAccount>(result.Account).Id;
        }
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        try
        {
            var actor = new Actor(client, id, await TokenAsync(client));
            using var login = await actor.PostAsync("/api/auth/login", new { email, password });
            login.EnsureSuccessStatusCode();
            actor.Token = await TokenAsync(client);
            return actor;
        }
        catch { client.Dispose(); throw; }
    }
    private static async Task<string> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(JsonOptions));
    }
    private static async Task ErrorAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
    private sealed class Actor(HttpClient client, Guid id, string token) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Id { get; } = id;
        public string Token { get; set; } = token;
        public async Task<HttpResponseMessage> PostAsync(string path, object body, string? csrf = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
            string selected = csrf ?? Token;
            if (selected.Length > 0) { request.Headers.Add("X-CSRF-TOKEN", selected); }
            return await Client.SendAsync(request);
        }
        public void Dispose() => Client.Dispose();
    }
}
