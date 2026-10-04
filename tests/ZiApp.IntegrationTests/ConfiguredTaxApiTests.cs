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

public sealed class ConfiguredTaxApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    [Theory]
    [InlineData(250, "10000", "0", "1800.00", "500.00")]
    [InlineData(-250, "-10000", "-10000", "0.00", "0.00")]
    [InlineData(0, "0", "0", "0.00", "0.00")]
    public async Task AnnualResultsTaxesAndNegativeLossesSurviveSaveReadAndExports(int gainUsd, string net,
        string loss, string investmentTax, string militaryTax)
    {
        using var actor = await ActorAsync();
        var report = await SourceAsync(actor, gainUsd);
        var settings = await SettingsAsync(actor);
        using var created = await actor.PostAsync(Path(report.PortfolioId), new CreateConfiguredTaxReportInput(report.Id, settings.Id));
        var saved = await ReadAsync<ConfiguredTaxDocument>(created, HttpStatusCode.Created);
        Assert.Equal(Path(report.PortfolioId) + "/" + saved.Id, created.Headers.Location!.AbsolutePath);
        Assert.Equal(net, saved.Amounts.NetProfitUah);
        Assert.Equal(loss, saved.Amounts.LossUah);
        Assert.Equal(investmentTax, saved.Amounts.InvestmentTaxUah);
        Assert.Equal(militaryTax, saved.Amounts.MilitaryTaxUah);
        Assert.Equal("9", saved.Settings.DividendIncomePercent);
        Assert.Equal("NotIncluded", saved.DividendStatus);
        Assert.Equal("UserConfigured", saved.Status);
        Assert.False(saved.IsTaxReady);
        string item = Path(report.PortfolioId) + "/" + saved.Id;
        using var read = await actor.Client.GetAsync(item);
        Assert.Equal(saved.Amounts, (await ReadAsync<ConfiguredTaxDocument>(read)).Amounts);
        Assert.True(read.Headers.CacheControl!.NoStore);
        string json = await actor.Client.GetStringAsync(item + "/export?format=json");
        string csv = await actor.Client.GetStringAsync(item + "/export?format=csv");
        Assert.Contains("\"LossUah\"", csv);
        Assert.Contains("\"" + loss + "\"", csv);
        Assert.Contains("\"'=SUM(1,2)\"", csv);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.ConfiguredTaxReports.SingleAsync(r => r.Id == saved.Id);
        Assert.Equal(stored.SnapshotJson, json);
        Assert.Equal(stored.SnapshotSha256, ReportSnapshot.Hash(json));
        Assert.Equal(report.InputSha256, saved.SourceReport.InputSha256);
        Assert.Equal((await db.TaxCalculationRuns.SingleAsync(r => r.Id == report.Id)).SnapshotSha256, saved.SourceSnapshotSha256);
        using var list = await actor.Client.GetAsync(Path(report.PortfolioId) + "?taxYear=2025&pageSize=1");
        Assert.Single((await ReadAsync<TradingPage<ConfiguredTaxListItem>>(list)).Items);
        using var status = await actor.Client.GetAsync(item + "/current-status");
        Assert.True((await ReadAsync<ConfiguredTaxCurrentStatus>(status)).UsesLatestSettings);
    }

    [Fact]
    public async Task YearsAndSettingRevisionsAreExplicitAndOldSnapshotsNeverChange()
    {
        using var actor = await ActorAsync();
        var report2024 = await SourceAsync(actor, 250m, 2024);
        var rates2024 = await SettingsAsync(actor, 2024, "10", "1.5");
        var rates2025 = await SettingsAsync(actor);
        using var wrongYear = await actor.PostAsync(Path(report2024.PortfolioId), new CreateConfiguredTaxReportInput(report2024.Id, rates2025.Id));
        await ErrorAsync(wrongYear, 400, "YearMismatch");
        var saved = await SaveAsync(actor, report2024, rates2024);
        Assert.Equal("1000.00", saved.Amounts.InvestmentTaxUah);
        Assert.Equal("150.00", saved.Amounts.MilitaryTaxUah);
        string item = Path(saved.PortfolioId) + "/" + saved.Id;
        string before = await actor.Client.GetStringAsync(item + "/export?format=json");
        var replacement = await SettingsAsync(actor, 2024, "20", "7");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var portfolio = await db.Portfolios.SingleAsync(p => p.Id == saved.PortfolioId);
            portfolio.Rename("Changed source metadata");
            portfolio.Archive();
            await db.SaveChangesAsync();
        }
        Assert.Equal(before, await actor.Client.GetStringAsync(item + "/export?format=json"));
        using var status = await actor.Client.GetAsync(item + "/current-status");
        var current = await ReadAsync<ConfiguredTaxCurrentStatus>(status);
        Assert.False(current.UsesLatestSettings);
        Assert.Equal(replacement.Id, current.LatestSettingsId);
        Assert.Equal("InputsChanged", current.SourceStatus);
        using var history = await actor.Client.GetAsync("/api/tax-settings/2024/history");
        Assert.Equal(2, (await ReadAsync<TradingPage<TaxSettingsDetails>>(history)).TotalCount);
        using var latest = await actor.Client.GetAsync("/api/tax-settings/2025");
        Assert.Equal(rates2025.Id, (await ReadAsync<TaxSettingsDetails>(latest)).Id);
        // Archived sources and explicitly selected older setting revisions remain usable.
        var next = await SaveAsync(actor, report2024, replacement);
        Assert.NotEqual(saved.Id, next.Id);
        Assert.Equal("2700.00", next.Amounts.TotalTaxUah);
        Assert.Equal("1150.00", (await SaveAsync(actor, report2024, rates2024)).Amounts.TotalTaxUah);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task AccountsCannotReadOrSelectOthersReportsOrSettings(AccountRole role)
    {
        using var owner = await ActorAsync();
        using var stranger = await ActorAsync(role);
        var source = await SourceAsync(owner);
        var ownerRates = await SettingsAsync(owner);
        var strangerSource = await SourceAsync(stranger);
        var strangerRates = await SettingsAsync(stranger);
        var saved = await SaveAsync(owner, source, ownerRates);
        using var settings = await stranger.Client.GetAsync("/api/tax-settings/2025");
        Assert.Equal(strangerRates.Id, (await ReadAsync<TaxSettingsDetails>(settings)).Id);
        foreach (var pair in new[]
        {
            (source, strangerRates), (strangerSource, ownerRates), (source, ownerRates)
        })
        {
            using var create = await stranger.PostAsync(Path(pair.Item1.PortfolioId), new CreateConfiguredTaxReportInput(pair.Item1.Id, pair.Item2.Id));
            Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        }
        foreach (string suffix in new[] { "", "/export", "/current-status" })
        {
            using var response = await stranger.Client.GetAsync(Path(source.PortfolioId) + "/" + saved.Id + suffix);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var list = await stranger.Client.GetAsync(Path(source.PortfolioId));
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task TaxesUseNetAnnualGainAfterLossesAndEmptyYearsRemainZero()
    {
        using var actor = await ActorAsync();
        var source = await SourceAsync(actor, 250m); // +10000 UAH.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Guid instrument = Guid.CreateVersion7();
            db.Instruments.Add(new(instrument, instrument.ToString("N")[..20], "TEST", "Loss", InstrumentType.Stock, "USD"));
            foreach (var (year, side, price) in new[] { (2024, TradeSide.Buy, 1000m), (2025, TradeSide.Sell, 900m) })
            {
                var at = new DateTimeOffset(year, 1, 1, 12, 0, 0, TimeSpan.Zero);
                var date = DateOnly.FromDateTime(at.DateTime);
                var rate = await db.ExchangeRates.SingleAsync(r => r.EffectiveDate == date && r.Source == NbuRateSource.Key);
                var trade = new InvestmentTransaction(Guid.CreateVersion7(), source.PortfolioId, instrument, null, side,
                    at, 1m, price, 0m, executedAtOriginal: at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
                trade.ResolveExchangeRate(rate, date, TradeRatePolicy.Version, actor.Id, DateTimeOffset.UtcNow);
                db.InvestmentTransactions.Add(trade);
            }
            await db.SaveChangesAsync();
        }
        using var newSourceResponse = await actor.PostAsync($"/api/portfolios/{source.PortfolioId}/tax-reports", new CreateReportInput(2025));
        var updated = await ReadAsync<TaxReportDocument>(newSourceResponse, HttpStatusCode.Created);
        var configured = await SaveAsync(actor, updated, await SettingsAsync(actor));
        Assert.Equal("6000", configured.Amounts.NetProfitUah);
        Assert.Equal("0", configured.Amounts.LossUah);
        Assert.Equal("1080.00", configured.Amounts.InvestmentTaxUah);
        Assert.Equal("300.00", configured.Amounts.MilitaryTaxUah);
        using var emptyResponse = await actor.PostAsync($"/api/portfolios/{source.PortfolioId}/tax-reports", new CreateReportInput(2023));
        var empty = await ReadAsync<TaxReportDocument>(emptyResponse, HttpStatusCode.Created);
        var emptyConfigured = await SaveAsync(actor, empty, await SettingsAsync(actor, 2023));
        Assert.Empty(emptyConfigured.SourceReport.Matches);
        Assert.Equal("0", emptyConfigured.Amounts.LossUah);
        Assert.Equal("0.00", emptyConfigured.Amounts.TotalTaxUah);
    }

    [Fact]
    public async Task AnonymousCsrfAndInactiveAccountsAreRejected()
    {
        using var anonymous = await fixture.Client.GetAsync("/api/tax-settings/2025");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var actor = await ActorAsync();
        var source = await SourceAsync(actor);
        var settings = await SettingsAsync(actor);
        using var missingSettingsCsrf = await actor.PostAsync("/api/tax-settings/2025", new SaveTaxSettingsInput("18", "5", "9"), "");
        Assert.Equal(HttpStatusCode.BadRequest, missingSettingsCsrf.StatusCode);
        using var missingReportCsrf = await actor.PostAsync(Path(source.PortfolioId), new CreateConfiguredTaxReportInput(source.Id, settings.Id), "");
        Assert.Equal(HttpStatusCode.BadRequest, missingReportCsrf.StatusCode);
        var saved = await SaveAsync(actor, source, settings);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(a => a.Id == actor.Id)).Deactivate();
            await db.SaveChangesAsync();
        }
        foreach (string path in new[] { "/api/tax-settings/2025", "/api/tax-settings/2025/history",
            Path(source.PortfolioId), Path(source.PortfolioId) + "/" + saved.Id,
            Path(source.PortfolioId) + "/" + saved.Id + "/export", Path(source.PortfolioId) + "/" + saved.Id + "/current-status" })
        {
            using var response = await actor.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var inactive = await actor.PostAsync("/api/tax-settings/2025", new SaveTaxSettingsInput("18", "5", "9"));
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        using var inactiveReport = await actor.PostAsync(Path(source.PortfolioId), new CreateConfiguredTaxReportInput(source.Id, settings.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveReport.StatusCode);
    }

    [Theory]
    [InlineData("bad-hash", "InvalidSource")]
    [InlineData("legacy", "LegacySnapshotUnavailable")]
    public async Task BadSourcesAreRejectedWithoutPartialReports(string mode, string code)
    {
        using var actor = await ActorAsync();
        var source = await SourceAsync(actor);
        var settings = await SettingsAsync(actor);
        Guid sourceId = source.Id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (mode == "legacy")
            {
                var legacy = new TaxCalculationRun(Guid.CreateVersion7(), source.PortfolioId, 2025, source.CalculationVersion, DateTimeOffset.UtcNow);
                db.TaxCalculationRuns.Add(legacy);
                await db.SaveChangesAsync();
                sourceId = legacy.Id;
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tax_calculation_runs SET snapshot_json = snapshot_json || ' ' WHERE id = {sourceId}");
            }
        }
        using var create = await actor.PostAsync(Path(source.PortfolioId), new CreateConfiguredTaxReportInput(sourceId, settings.Id));
        await ErrorAsync(create, 409, code);
        using var list = await actor.Client.GetAsync(Path(source.PortfolioId));
        Assert.Empty((await ReadAsync<TradingPage<ConfiguredTaxListItem>>(list)).Items);
    }

    [Fact]
    public async Task ConfiguredSnapshotTamperingFailsReadExportAndStatus()
    {
        using var actor = await ActorAsync();
        var source = await SourceAsync(actor);
        var saved = await SaveAsync(actor, source, await SettingsAsync(actor));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE configured_tax_reports SET snapshot_json = snapshot_json || ' ' WHERE id = {saved.Id}");
        }
        foreach (string suffix in new[] { "", "/export", "/current-status" })
        {
            using var response = await actor.Client.GetAsync(Path(source.PortfolioId) + "/" + saved.Id + suffix);
            await ErrorAsync(response, 409, "InvalidSnapshot");
        }
    }

    [Theory]
    [InlineData("18.00001")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("18,5")]
    [InlineData(null)]
    public async Task InvalidSettingsDoNotSave(string? rate)
    {
        using var actor = await ActorAsync();
        using var response = await actor.PostAsync("/api/tax-settings/2025", new SaveTaxSettingsInput(rate!, "5", "9"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var latest = await actor.Client.GetAsync("/api/tax-settings/2025");
        Assert.Equal(HttpStatusCode.NotFound, latest.StatusCode);
    }

    [Theory]
    [InlineData("/api/tax-settings/1999")]
    [InlineData("/api/tax-settings/9999")]
    [InlineData("/api/tax-settings/2025/history?page=0")]
    [InlineData("/api/tax-settings/2025/history?pageSize=101")]
    public async Task InvalidYearOrPaginationIsRejected(string path)
    {
        using var actor = await ActorAsync();
        using var response = await actor.Client.GetAsync(path);
        await ErrorAsync(response, 400, "InvalidInput");
    }

    private static string Path(Guid portfolio) => $"/api/portfolios/{portfolio}/configured-tax-reports";

    private static async Task<TaxSettingsDetails> SettingsAsync(Actor actor, int year = 2025, string income = "18", string military = "5")
    {
        using var response = await actor.PostAsync($"/api/tax-settings/{year}", new SaveTaxSettingsInput(income, military, "9"));
        return await ReadAsync<TaxSettingsDetails>(response, HttpStatusCode.Created);
    }

    private static async Task<ConfiguredTaxDocument> SaveAsync(Actor actor, TaxReportDocument source, TaxSettingsDetails settings)
    {
        using var response = await actor.PostAsync(Path(source.PortfolioId), new CreateConfiguredTaxReportInput(source.Id, settings.Id));
        return await ReadAsync<ConfiguredTaxDocument>(response, HttpStatusCode.Created);
    }

    private async Task<TaxReportDocument> SourceAsync(Actor actor, decimal profitUsd = 250m, int year = 2025)
    {
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(new Portfolio(portfolio, actor.Id, "=SUM(1,2)", "USD", DateTimeOffset.UtcNow),
                new Instrument(instrument, instrument.ToString("N")[..20], "TEST", "Synthetic", InstrumentType.Stock, "USD"));
            foreach (var (side, at, price) in new[] { (TradeSide.Buy, new DateTimeOffset(year - 1, 1, 1, 12, 0, 0, TimeSpan.Zero), 1000m),
                (TradeSide.Sell, new DateTimeOffset(year, 1, 1, 12, 0, 0, TimeSpan.Zero), 1000m + profitUsd) })
            {
                var date = DateOnly.FromDateTime(at.DateTime);
                var rate = await db.ExchangeRates.SingleOrDefaultAsync(r => r.EffectiveDate == date && r.Source == NbuRateSource.Key);
                if (rate is null)
                {
                    rate = new ExchangeRate(Guid.CreateVersion7(), "USD", date, 40m, NbuRateSource.Key, DateTimeOffset.UtcNow,
                        date, "https://bank.gov.ua/test-fixture", ReportSnapshot.Hash("[]"), "[]");
                    db.ExchangeRates.Add(rate);
                }
                var trade = new InvestmentTransaction(Guid.CreateVersion7(), portfolio, instrument, null, side,
                    at, 1m, price, 0m, executedAtOriginal: at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
                trade.ResolveExchangeRate(rate, date, TradeRatePolicy.Version, actor.Id, DateTimeOffset.UtcNow);
                db.InvestmentTransactions.Add(trade);
            }
            await db.SaveChangesAsync();
        }
        using var response = await actor.PostAsync($"/api/portfolios/{portfolio}/tax-reports", new CreateReportInput(year));
        return await ReadAsync<TaxReportDocument>(response, HttpStatusCode.Created);
    }

    private async Task<Actor> ActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Configured!Testing123";
        string email = $"configured-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var provision = await scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>()
                .ProvisionAsync(new(email, "Configured reporter", password, role, SupportedLanguage.English));
            Assert.True(provision.Succeeded);
            id = Assert.IsType<ProvisionedAccount>(provision.Account).Id;
        }
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
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
        public async Task<HttpResponseMessage> PostAsync(string path, object input, string? csrf = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
            string selected = csrf ?? Token;
            if (selected.Length > 0) { request.Headers.Add("X-CSRF-TOKEN", selected); }
            return await Client.SendAsync(request);
        }
        public void Dispose() => Client.Dispose();
    }
}
