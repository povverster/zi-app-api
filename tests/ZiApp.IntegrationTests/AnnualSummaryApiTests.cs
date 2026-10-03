using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Application.Accounts;
using ZiApp.Application.ExchangeRates;
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

public sealed class AnnualSummaryApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Path = "/api/annual-summaries";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task SignedSubtotalsArchivedSourcesClaimsAndExportsArePreserved()
    {
        using var owner = await ActorAsync();
        var gain = await SeedAsync(owner, 250m);
        var loss = await SeedAsync(owner, -100m);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Portfolios.SingleAsync(p => p.Id == loss.PortfolioId)).Archive();
            db.Portfolios.Add(new(Guid.CreateVersion7(), owner.Id, "Omitted", "USD", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var input = Input(gain, loss) with
        {
            ExternalCoverage = "Provided",
            ExternalInvestments = [new("statement-A", "External activity", "Foreign stocks 2025", "-15.123456789012", "paper statement")],
            PriorLossCoverage = "Provided",
            PriorLossClaims = [new(2024, "8000", "1000", "Prior filing, page 3")]
        };
        using var response = await owner.PostAsync(Path, input);
        var saved = await ReadAsync<AnnualSummaryDocument>(response, HttpStatusCode.Created);
        Assert.EndsWith("/" + saved.Id, response.Headers.Location!.ToString());
        Assert.Equal(owner.Id, saved.OwnerAccountId);
        Assert.Equal(owner.Id, saved.CreatedByAccountId);
        Assert.Equal("6000", saved.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal("-15.123456789012", saved.ExternalProfitSubtotalUah);
        Assert.Equal(input.PriorLossClaims, saved.Inputs.PriorLossClaims);
        Assert.Single(saved.OmittedPortfolioIds);
        Assert.Contains(saved.PortfolioInventory, p => p.Id == loss.PortfolioId && p.IsArchived);
        Assert.Contains("OmittedPortfolios", saved.ReviewIssues);
        Assert.Contains("PriorLossClaimsNotApplied", saved.ReviewIssues);
        Assert.False(saved.IsTaxReady);
        using var exported = await owner.Client.GetAsync($"{Path}/{saved.Id}/export");
        var exportedText = await exported.Content.ReadAsStringAsync();
        Assert.Equal("application/json", exported.Content.Headers.ContentType!.MediaType);
        Assert.Contains("ziapp-annual-preparation-2025-", exported.Content.Headers.ContentDisposition!.FileNameStar);
        await using var verify = fixture.Services.CreateAsyncScope();
        var stored = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().AnnualPreparationDrafts.SingleAsync(r => r.Id == saved.Id);
        Assert.Equal(stored.SnapshotJson, exportedText);
        Assert.Equal(stored.SnapshotSha256, ReportSnapshot.Hash(exportedText));
        Assert.Equal(gain.InputSha256, saved.SourceReports[0].Report.InputSha256);
        using var listed = await owner.Client.GetAsync(Path + "?page=1&pageSize=1");
        var page = await ReadAsync<TradingPage<AnnualSummaryListItem>>(listed);
        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        using var status = await owner.Client.GetAsync($"{Path}/{saved.Id}/current-status");
        Assert.Equal("Current", (await ReadAsync<AnnualSummaryCurrentStatus>(status)).Status);
    }

    [Theory]
    [InlineData("Unknown", null)]
    [InlineData("None", "0")]
    public async Task EmptyYearsRetainExplicitUnknownVersusZero(string coverage, string? expected)
    {
        using var owner = await ActorAsync();
        var input = Input() with { ExternalCoverage = coverage, PriorLossCoverage = coverage };
        using var response = await owner.PostAsync(Path, input);
        var saved = await ReadAsync<AnnualSummaryDocument>(response, HttpStatusCode.Created);
        Assert.Equal("0", saved.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal(expected, saved.ExternalProfitSubtotalUah);
        Assert.False(saved.IsTaxReady);
        Assert.Empty(saved.SourceReports);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task AnotherAccountCannotSelectReadExportOrInspectAnnualSources(AccountRole role)
    {
        using var owner = await ActorAsync();
        using var stranger = await ActorAsync(role);
        var report = await SeedAsync(owner);
        using var created = await owner.PostAsync(Path, Input(report));
        var saved = await ReadAsync<AnnualSummaryDocument>(created, HttpStatusCode.Created);
        using var foreignSelection = await stranger.PostAsync(Path, Input(report));
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);
        foreach (string suffix in new[] { "", "/export", "/current-status" })
        {
            using var response = await stranger.Client.GetAsync($"{Path}/{saved.Id}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var list = await stranger.Client.GetAsync(Path);
        Assert.Empty((await ReadAsync<TradingPage<AnnualSummaryListItem>>(list)).Items);
    }

    [Fact]
    public async Task CsrfAnonymousAndInactiveAccountsAreRejected()
    {
        using var anonymous = await fixture.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var owner = await ActorAsync();
        using var noCsrf = await owner.PostAsync(Path, Input(), "");
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var created = await owner.PostAsync(Path, Input());
        var saved = await ReadAsync<AnnualSummaryDocument>(created, HttpStatusCode.Created);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(a => a.Id == owner.Id)).Deactivate();
            await db.SaveChangesAsync();
        }
        foreach (string path in new[] { Path, $"{Path}/{saved.Id}", $"{Path}/{saved.Id}/export", $"{Path}/{saved.Id}/current-status" })
        {
            using var response = await owner.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var inactivePost = await owner.PostAsync(Path, Input());
        Assert.Equal(HttpStatusCode.Unauthorized, inactivePost.StatusCode);
    }

    [Theory]
    [InlineData("duplicate", 400, "InvalidInput")]
    [InlineData("same-portfolio", 400, "InvalidInput")]
    [InlineData("wrong-year", 400, "InvalidInput")]
    [InlineData("bad-hash", 409, "InvalidSource")]
    [InlineData("legacy", 409, "LegacySnapshotUnavailable")]
    public async Task InvalidSourcesFailWithoutSavingAnAnnualDraft(string mode, int status, string code)
    {
        using var owner = await ActorAsync();
        var report = await SeedAsync(owner);
        var input = Input(report);
        if (mode == "duplicate") { input = input with { Reports = [input.Reports[0], input.Reports[0]] }; }
        else if (mode is "same-portfolio" or "wrong-year")
        {
            using var created = await owner.PostAsync($"/api/portfolios/{report.PortfolioId}/tax-reports",
                new CreateReportInput(mode == "wrong-year" ? 2024 : 2025));
            var other = await ReadAsync<TaxReportDocument>(created, HttpStatusCode.Created);
            input = mode == "wrong-year" ? Input(other) : Input(report, other);
        }
        else
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (mode == "bad-hash")
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tax_calculation_runs SET snapshot_json = snapshot_json || ' ' WHERE id = {report.Id}");
            }
            else
            {
                var legacy = new TaxCalculationRun(Guid.CreateVersion7(), report.PortfolioId, 2025, report.CalculationVersion, DateTimeOffset.UtcNow);
                db.TaxCalculationRuns.Add(legacy);
                await db.SaveChangesAsync();
                input = input with { Reports = [new(report.PortfolioId, legacy.Id, null)] };
            }
        }
        using var response = await owner.PostAsync(Path, input);
        await ErrorAsync(response, status, code);
        await using var check = fixture.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .AnnualPreparationDrafts.AnyAsync(a => a.OwnerAccountId == owner.Id));
    }

    [Fact]
    public async Task CoverageAndSourceChangesDoNotRewriteSavedExports()
    {
        using var owner = await ActorAsync();
        var report = await SeedAsync(owner);
        using var response = await owner.PostAsync(Path, Input(report));
        var annual = await ReadAsync<AnnualSummaryDocument>(response, HttpStatusCode.Created);
        string before = await owner.Client.GetStringAsync($"{Path}/{annual.Id}/export");
        var originalSale = Assert.Single(report.Inputs.Trades, t => t.Side == "Sell");
        using var correction = await owner.PostAsync($"/api/portfolios/{report.PortfolioId}/trades/{originalSale.Id}/corrections",
            new CorrectTradeInput(new(originalSale.InstrumentId, TradeSide.Sell, originalSale.ExecutedAtOriginal!,
                originalSale.Quantity, "1020", "0"), "Correct sale price"));
        var replacement = await ReadAsync<TradeDetails>(correction, HttpStatusCode.Created);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rate = await db.ExchangeRates.SingleAsync(r => r.Id == originalSale.ExchangeRateId);
            (await db.InvestmentTransactions.SingleAsync(t => t.Id == replacement.Id))
                .ResolveExchangeRate(rate, originalSale.BrokerDate, TradeRatePolicy.Version, owner.Id, DateTimeOffset.UtcNow);
            (await db.Portfolios.SingleAsync(p => p.Id == report.PortfolioId)).Rename("Renamed after capture");
            db.Portfolios.Add(new(Guid.CreateVersion7(), owner.Id, "Added after capture", "USD", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        using var changed = await owner.Client.GetAsync($"{Path}/{annual.Id}/current-status");
        var current = await ReadAsync<AnnualSummaryCurrentStatus>(changed);
        Assert.Equal("ReviewRequired", current.Status);
        Assert.True(current.PortfolioInventoryChanged);
        Assert.Equal("InputsChanged", Assert.Single(current.Sources).Status);
        using var newReportResponse = await owner.PostAsync($"/api/portfolios/{report.PortfolioId}/tax-reports", new CreateReportInput(2025));
        var newReport = await ReadAsync<TaxReportDocument>(newReportResponse, HttpStatusCode.Created);
        using var newAnnualResponse = await owner.PostAsync(Path, Input(newReport));
        var newer = await ReadAsync<AnnualSummaryDocument>(newAnnualResponse, HttpStatusCode.Created);
        Assert.NotEqual(annual.Id, newer.Id);
        Assert.Equal("400", annual.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal("800", newer.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal(before, await owner.Client.GetStringAsync($"{Path}/{annual.Id}/export"));
        using var old = await owner.Client.GetAsync($"/api/portfolios/{report.PortfolioId}/tax-reports/{report.Id}");
        Assert.Equal(report.InputSha256, (await ReadAsync<TaxReportDocument>(old)).InputSha256);
    }

    [Fact]
    public async Task SourceIntegrityChangesAreReportedWithoutLosingTheAnnualSnapshot()
    {
        using var owner = await ActorAsync();
        var report = await SeedAsync(owner);
        using var response = await owner.PostAsync(Path, Input(report));
        var annual = await ReadAsync<AnnualSummaryDocument>(response, HttpStatusCode.Created);
        string before = await owner.Client.GetStringAsync($"{Path}/{annual.Id}/export");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tax_calculation_runs SET snapshot_json = snapshot_json || ' ' WHERE id = {report.Id}");
        }
        using var status = await owner.Client.GetAsync($"{Path}/{annual.Id}/current-status");
        var current = await ReadAsync<AnnualSummaryCurrentStatus>(status);
        Assert.Equal("ReviewRequired", current.Status);
        Assert.Equal("SourceInvalid", Assert.Single(current.Sources).Status);
        Assert.Null(current.Sources[0].MatchesCurrentInputs);
        Assert.Equal(before, await owner.Client.GetStringAsync($"{Path}/{annual.Id}/export"));
    }

    [Fact]
    public async Task TamperedAnnualSnapshotCannotBeReadOrExported()
    {
        using var owner = await ActorAsync();
        using var created = await owner.PostAsync(Path, Input());
        var saved = await ReadAsync<AnnualSummaryDocument>(created, HttpStatusCode.Created);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE annual_preparation_drafts SET snapshot_json = snapshot_json || ' ' WHERE id = {saved.Id}");
        }
        foreach (string suffix in new[] { "", "/export", "/current-status" })
        {
            using var response = await owner.Client.GetAsync($"{Path}/{saved.Id}{suffix}");
            await ErrorAsync(response, 409, "InvalidSnapshot");
        }
    }

    [Fact]
    public async Task BndBndxAndCorrectedBxmtSamplesReconcileThroughBothReportApis()
    {
        // Numerical fixtures transcribed from read-only zi-samples, sheet "2024 - 2025".
        // BXMT C5 is overridden only here by the user's confirmed 18.08 USD; see sample audit.
        using var owner = await ActorAsync();
        var bnd = await SampleAsync(owner, "BND",
            [new("2024-07-25T16:31:17Z", 1m, 72.6m, 2.02m, 41.2174m),
             new("2025-06-02T15:56:38Z", 1m, 72.42m, 2.02m, 41.5261m)],
            new("2025-10-10T16:31:16Z", 2m, 74.42m, 2.04m, 41.5062m));
        var bndx = await SampleAsync(owner, "BNDX",
            [new("2024-10-15T16:30:04Z", 1m, 50.07m, 2.02m, 41.1963m),
             new("2025-01-06T16:46:22Z", 2m, 48.93m, 2.04m, 42.0889m)],
            new("2025-10-10T16:32:08Z", 3m, 49.49m, 2.06m, 41.5062m));
        var bxmt = await SampleAsync(owner, "BXMT",
            [new("2024-12-05T19:26:46Z", 2m, 18.65m, 2.04m, 41.6628m),
             new("2024-12-23T15:33:13Z", 1m, 18.08m, 2.02m, 41.8761m)],
            new("2025-01-10T19:50:04Z", 3m, 17.01m, 2.06m, 42.2825m));
        Assert.Equal("-73.735112", bnd.Totals.ProfitUah);
        Assert.Equal("-273.673635", bndx.Totals.ProfitUah);
        Assert.Equal("-410.150137", bxmt.Totals.ProfitUah);
        using var created = await owner.PostAsync(Path, Input(bnd, bndx, bxmt) with { ExternalCoverage = "None", PriorLossCoverage = "None" });
        var annual = await ReadAsync<AnnualSummaryDocument>(created, HttpStatusCode.Created);
        Assert.Equal("-757.558884", annual.SelectedReportsSubtotal.ProfitUah);
        Assert.Equal("0", annual.ExternalProfitSubtotalUah);
        Assert.False(annual.IsTaxReady);
        Assert.Equal(3, annual.SourceReports.Count);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=2147483647&pageSize=100")]
    public async Task InvalidPaginationIsRejected(string query)
    {
        using var owner = await ActorAsync();
        using var response = await owner.Client.GetAsync(Path + query);
        await ErrorAsync(response, 400, "InvalidInput");
    }

    private Task<TaxReportDocument> SeedAsync(Actor owner, decimal profitUsd = 10m) => SampleAsync(owner, "SYNTHETIC",
        [new("2024-01-01T12:00:00Z", 1m, 1000m, 0m, 40m)],
        new("2025-01-01T12:00:00Z", 1m, 1000m + profitUsd, 0m, 40m));

    private async Task<TaxReportDocument> SampleAsync(Actor owner, string label, SampleTrade[] buys, SampleTrade sale)
    {
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(new Portfolio(portfolio, owner.Id, label + portfolio, "USD", DateTimeOffset.UtcNow),
                new Instrument(instrument, instrument.ToString("N")[..20], "TEST", label, InstrumentType.Stock, "USD"));
            foreach (var buy in buys) { await TradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Buy, buy); }
            await TradeAsync(db, owner.Id, portfolio, instrument, TradeSide.Sell, sale);
            await db.SaveChangesAsync();
        }
        using var response = await owner.PostAsync($"/api/portfolios/{portfolio}/tax-reports", new CreateReportInput(2025));
        return await ReadAsync<TaxReportDocument>(response, HttpStatusCode.Created);
    }
    private sealed record SampleTrade(string At, decimal Quantity, decimal Price, decimal Fee, decimal Rate);
    private static async Task TradeAsync(ApplicationDbContext db, Guid owner, Guid portfolio, Guid instrument, TradeSide side, SampleTrade sample)
    {
        var instant = DateTimeOffset.Parse(sample.At, CultureInfo.InvariantCulture);
        var date = DateOnly.FromDateTime(instant.DateTime);
        var rate = db.ExchangeRates.Local.SingleOrDefault(r => r.EffectiveDate == date && r.Source == NbuRateSource.Key)
            ?? await db.ExchangeRates.SingleOrDefaultAsync(r => r.EffectiveDate == date && r.Source == NbuRateSource.Key);
        if (rate is null)
        {
            rate = new(Guid.CreateVersion7(), "USD", date, sample.Rate, NbuRateSource.Key, DateTimeOffset.UtcNow,
                date, "https://bank.gov.ua/test-fixture", ReportSnapshot.Hash("[]"), "[]");
            db.ExchangeRates.Add(rate);
        }
        Assert.Equal(sample.Rate, rate.RateToUah);
        var trade = new InvestmentTransaction(Guid.CreateVersion7(), portfolio, instrument, null, side,
            instant.ToUniversalTime(), sample.Quantity, sample.Price, sample.Fee, executedAtOriginal: sample.At);
        trade.ResolveExchangeRate(rate, date, TradeRatePolicy.Version, owner, DateTimeOffset.UtcNow);
        db.InvestmentTransactions.Add(trade);
    }
    private static CreateAnnualSummaryInput Input(params TaxReportDocument[] reports) => new(2025,
        reports.Select(r => new AnnualReportSelection(r.PortfolioId, r.Id, "sample-broker")).ToArray(),
        "Unknown", [], "Unknown", [], true, true);
    private async Task<Actor> ActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Annual!Testing123";
        string email = $"annual-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>()
                .ProvisionAsync(new(email, "Annual reporter", password, role, SupportedLanguage.English));
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
    private static async Task ErrorAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
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
