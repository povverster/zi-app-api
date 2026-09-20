using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ZiApp.Application.Accounts;
using ZiApp.Application.ExchangeRates;
using ZiApp.Application.Trading;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.ExchangeRates;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class ExchangeRateTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static int s_daySequence;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData("GET", "/api/exchange-rates/usd/2025-01-05")]
    [InlineData("POST", "/api/exchange-rates/usd/2025-01-05/fetch")]
    [InlineData("GET", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades/00000000-0000-0000-0000-000000000002/exchange-rate")]
    [InlineData("POST", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades/00000000-0000-0000-0000-000000000002/exchange-rate/resolve")]
    public async Task AnonymousRequestsAreRejected(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResolutionPreservesBrokerDateAndProvenanceAndIsIdempotent()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        using var pending = await actor.Client.GetAsync(seed.Path);
        Assert.Equal("Pending", (await ReadAsync<TradeRateDetails>(pending)).Status);
        Assert.Equal(0, app.Handler.Calls);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve", new { date = "2000-01-01", exchangeRateId = Guid.CreateVersion7() });
        var selected = await ReadAsync<TradeRateDetails>(resolve);
        Assert.Equal(app.Day, selected.SelectedDate);
        Assert.Equal("Resolved", selected.Status);
        Assert.Equal(actor.Id, selected.ResolvedByAccountId);
        Assert.Equal(TradeRatePolicy.Version, selected.PolicyVersion);
        Assert.False(selected.IsTaxReady);
        Assert.NotNull(selected.Rate);
        Assert.Equal("42.0385", selected.Rate.RateToUah);
        Assert.Equal(app.Day, selected.Rate.EffectiveDate);
        Assert.Equal(NbuRateSource.AttributionUrl, selected.Rate.AttributionUrl);
        Assert.NotNull(selected.Rate.ResponseSha256);
        Assert.NotNull(selected.ResolvedAtUtc);
        Assert.Equal(1, app.Handler.Calls);
        using var retry = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal(selected, await ReadAsync<TradeRateDetails>(retry));
        using var get = await actor.Client.GetAsync(seed.Path);
        Assert.Equal(selected, await ReadAsync<TradeRateDetails>(get));
        using var tradeResponse = await actor.Client.GetAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.TradeId}");
        var trade = await ReadAsync<TradeDetails>(tradeResponse);
        Assert.Equal("Resolved", trade.RateStatus);
        Assert.Equal("1.25", trade.Quantity);
        Assert.Equal(seed.Original, trade.ExecutedAtOriginal);
        Assert.Equal(app.Day.AddDays(1), DateOnly.FromDateTime(trade.ExecutedAtUtc.UtcDateTime));
        using var cached = await actor.Client.GetAsync(app.RatePath);
        Assert.Equal(selected.Rate, await ReadAsync<ExchangeRateDetails>(cached));
        app.Handler.Reply = _ => throw new InvalidOperationException("A cache hit must not call NBU.");
        using var fetchedAgain = await actor.PostAsync(app.RatePath + "/fetch");
        Assert.Equal(selected.Rate, await ReadAsync<ExchangeRateDetails>(fetchedAgain));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.ExchangeRates.SingleAsync(item => item.Id == selected.Rate.Id);
        Assert.Equal(NbuRateClientTests.Payload(app.Day), stored.RawResponseJson);
    }

    [Fact]
    public async Task LegacyUnlinkedTradeUsesStoredDateWithoutConversion()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day, legacy: true);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve");
        var selected = await ReadAsync<TradeRateDetails>(resolve);
        Assert.Equal(app.Day, selected.SelectedDate);
        Assert.Equal(TradeRatePolicy.LegacyVersion, selected.PolicyVersion);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task ForeignAndMissingTradesCannotBeReadOrResolved(AccountRole role)
    {
        using TestApp app = App();
        using Actor owner = await ActorAsync(app);
        using Actor stranger = await ActorAsync(app, role);
        var seed = await SeedAsync(owner, app.Day);
        using var read = await stranger.Client.GetAsync(seed.Path);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var resolve = await stranger.PostAsync(seed.Path + "/resolve");
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
        using var missing = await owner.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{Guid.CreateVersion7()}/exchange-rate/resolve");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, app.Handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task MutationsRequireCsrf(string token)
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        using var fetch = await actor.PostAsync(app.RatePath + "/fetch", token: token);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve", token: token);
        Assert.Equal(HttpStatusCode.BadRequest, fetch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, resolve.StatusCode);
        Assert.Equal(0, app.Handler.Calls);
    }

    [Fact]
    public async Task DisabledAccountCannotReadFetchOrResolve()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(item => item.Id == actor.Id)).Deactivate();
            await db.SaveChangesAsync();
        }

        using var get = await actor.Client.GetAsync(app.RatePath);
        using var trade = await actor.Client.GetAsync(seed.Path);
        using var fetch = await actor.PostAsync(app.RatePath + "/fetch");
        using var resolve = await actor.PostAsync(seed.Path + "/resolve");
        foreach (var response in new[] { get, trade, fetch, resolve }) { Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        Assert.Equal(0, app.Handler.Calls);
    }

    [Theory]
    [InlineData("[]", 404)]
    [InlineData("not json", 502)]
    [InlineData("unavailable", 503)]
    public async Task UpstreamFailureDoesNotAssignOrCacheRates(string body, int status)
    {
        using TestApp app = App();
        app.Handler.Reply = _ => Task.FromResult(body == "unavailable"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : NbuRateClientTests.Response(body));
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal((HttpStatusCode)status, resolve.StatusCode);
        using var get = await actor.Client.GetAsync(seed.Path);
        Assert.Equal("Pending", (await ReadAsync<TradeRateDetails>(get)).Status);
        using var cached = await actor.Client.GetAsync(app.RatePath);
        Assert.Equal(HttpStatusCode.NotFound, cached.StatusCode);
    }

    [Theory]
    [InlineData("1996-09-01")]
    [InlineData("2999-01-01")]
    [InlineData("2025-02-30")]
    public async Task InvalidAndFutureDatesAreRejected(string date)
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        using var response = await actor.PostAsync($"/api/exchange-rates/usd/{date}/fetch");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Handler.Calls);
    }

    [Fact]
    public async Task ConcurrentResolutionKeepsOneCacheRecordAndOneAssignment()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        var responses = await Task.WhenAll(actor.PostAsync(seed.Path + "/resolve"), actor.PostAsync(seed.Path + "/resolve"));
        try
        {
            var first = await ReadAsync<TradeRateDetails>(responses[0]);
            Assert.Equal(first, await ReadAsync<TradeRateDetails>(responses[1]));
        }
        finally { foreach (var response in responses) { response.Dispose(); } }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ExchangeRates.CountAsync(item => item.Source == NbuRateSource.Key && item.EffectiveDate == app.Day));
    }

    [Fact]
    public async Task ExistingRateLinksArePreservedAndNotRelabeledAsVerified()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day, legacy: true, linked: true);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        using var get = await actor.Client.GetAsync(seed.Path);
        var existing = await ReadAsync<TradeRateDetails>(get);
        Assert.Equal("LinkedUnverified", existing.Status);
        Assert.NotNull(existing.Rate);
        Assert.Equal("39.25", existing.Rate.RateToUah);
        Assert.Null(existing.Rate.ResponseSha256);
        Assert.Null(existing.PolicyVersion);
        Assert.Equal(0, app.Handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangesDuringNbuFetchAreRecheckedUnderPortfolioLock(bool archive)
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Handler.Reply = async _ =>
        {
            entered.SetResult();
            await release.Task;
            return NbuRateClientTests.Response(NbuRateClientTests.Payload(app.Day));
        };
        Task<HttpResponseMessage> resolution = actor.PostAsync(seed.Path + "/resolve");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (archive)
            {
                using var response = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/archive");
                response.EnsureSuccessStatusCode();
            }
            else
            {
                var input = new TradeInput(seed.InstrumentId, TradeSide.Buy, seed.Original!, "1.25", "90", "1");
                using var response = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.TradeId}/corrections",
                    new CorrectTradeInput(input, "Corrected during rate retrieval"));
                response.EnsureSuccessStatusCode();
            }
        }
        finally { release.TrySetResult(); }
        using var result = await resolution;
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        using var original = await actor.Client.GetAsync(seed.Path);
        Assert.Equal("Pending", (await ReadAsync<TradeRateDetails>(original)).Status);
    }

    [Fact]
    public async Task ArchivedReadsWorkButResolutionWaitsForRestore()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        using var archive = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/archive");
        archive.EnsureSuccessStatusCode();
        using var read = await actor.Client.GetAsync(seed.Path);
        Assert.Equal("Pending", (await ReadAsync<TradeRateDetails>(read)).Status);
        using var blocked = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(0, app.Handler.Calls);
        using var restore = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/restore");
        restore.EnsureSuccessStatusCode();
        using var resolved = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal("Resolved", (await ReadAsync<TradeRateDetails>(resolved)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheInsertRaceKeepsFirstResponseAndRejectsDifferentValues(bool conflict)
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        using var fetch = await actor.PostAsync(app.RatePath + "/fetch");
        var original = await ReadAsync<ExchangeRateDetails>(fetch);
        await using var scope = fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExchangeRateRepository>();
        var candidate = new ExchangeRate(Guid.CreateVersion7(), "USD", app.Day, conflict ? 43m : 42.0385m,
            NbuRateSource.Key, DateTimeOffset.UtcNow, original.CalculationDate, original.SourceUrl,
            original.ResponseSha256, NbuRateClientTests.Payload(app.Day));
        var result = await repository.CacheAsync(candidate, CancellationToken.None);
        if (conflict)
        {
            Assert.Null(result.Value);
            Assert.Equal(RateError.RateConflict, result.Error);
        }
        else
        {
            Assert.Equal(original.Id, Assert.IsType<ExchangeRate>(result.Value).Id);
        }
        Assert.Equal(original, ExchangeRateDetails.From(Assert.IsType<ExchangeRate>(await repository.FindAsync(app.Day, CancellationToken.None))));
    }

    [Fact]
    public async Task CorrectionPreservesResolvedOriginalAndStartsReplacementPending()
    {
        using TestApp app = App();
        using Actor actor = await ActorAsync(app);
        var seed = await SeedAsync(actor, app.Day);
        using var resolve = await actor.PostAsync(seed.Path + "/resolve");
        var original = await ReadAsync<TradeRateDetails>(resolve);
        var input = new TradeInput(seed.InstrumentId, TradeSide.Buy, seed.Original!, "1.25", "90", "1");
        using var correction = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.TradeId}/corrections",
            new CorrectTradeInput(input, "Correct broker price after rate resolution"));
        var replacement = await ReadAsync<TradeDetails>(correction);
        Assert.Equal("Pending", replacement.RateStatus);
        Assert.Null(replacement.ExchangeRateId);
        using var read = await actor.Client.GetAsync(seed.Path);
        Assert.Equal(original, await ReadAsync<TradeRateDetails>(read));
        using var blocked = await actor.PostAsync(seed.Path + "/resolve");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var current = await actor.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{replacement.Id}/exchange-rate/resolve");
        Assert.Equal(original.Rate, (await ReadAsync<TradeRateDetails>(current)).Rate);
        Assert.Equal(1, app.Handler.Calls);
    }

    private TestApp App()
    {
        DateOnly day = new DateOnly(2021, 1, 1).AddDays(Interlocked.Increment(ref s_daySequence));
        var handler = new NbuTestHandler(request =>
        {
            Assert.Equal(NbuRateClient.SourceUri(day), request.RequestUri);
            return Task.FromResult(NbuRateClientTests.Response(NbuRateClientTests.Payload(day)));
        });
        var http = new HttpClient(handler);
        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INbuRateClient>();
            services.AddSingleton<INbuRateClient>(new NbuRateClient(http, TimeProvider.System));
        }));
        return new(factory, http, handler, day);
    }

    private async Task<Actor> ActorAsync(TestApp app, AccountRole role = AccountRole.User)
    {
        const string password = "Rates!Testing123";
        string email = $"rates-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
            var result = await service.ProvisionAsync(new(email, "Rate Tester", password, role, SupportedLanguage.English));
            Assert.True(result.Succeeded);
            id = Assert.IsType<ProvisionedAccount>(result.Account).Id;
        }
        var client = app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
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

    private async Task<Seed> SeedAsync(Actor actor, DateOnly day, bool legacy = false, bool linked = false)
    {
        Guid portfolio = Guid.CreateVersion7();
        Guid instrument = Guid.CreateVersion7();
        Guid trade = Guid.CreateVersion7();
        Guid? rateId = linked ? Guid.CreateVersion7() : null;
        string? original = legacy ? null : string.Create(CultureInfo.InvariantCulture, $"{day:yyyy-MM-dd}T23:30:00-05:00");
        DateTimeOffset instant = original is null ? new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero)
            : DateTimeOffset.Parse(original, CultureInfo.InvariantCulture).ToUniversalTime();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(new Portfolio(portfolio, actor.Id, portfolio.ToString(), "USD", DateTimeOffset.UtcNow),
            new Instrument(instrument, instrument.ToString("N")[..20], "TEST", "Test USD", InstrumentType.Stock, "USD"),
            new InvestmentTransaction(trade, portfolio, instrument, rateId, TradeSide.Buy, instant, 1.25m, 100m, 1m, executedAtOriginal: original));
        if (rateId is not null) { db.ExchangeRates.Add(new ExchangeRate(rateId.Value, "USD", day, 39.25m, "LegacyTest", DateTimeOffset.UtcNow)); }
        await db.SaveChangesAsync();
        return new(portfolio, instrument, trade, original);
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(document.RootElement.GetProperty("token").GetString());
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(JsonOptions));
    }

    private sealed record Seed(Guid PortfolioId, Guid InstrumentId, Guid TradeId, string? Original)
    {
        public string Path => $"/api/portfolios/{PortfolioId}/trades/{TradeId}/exchange-rate";
    }

    private sealed class TestApp(WebApplicationFactory<Program> factory, HttpClient http, NbuTestHandler handler, DateOnly day) : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public NbuTestHandler Handler { get; } = handler;
        public DateOnly Day { get; } = day;
        public string RatePath => string.Create(CultureInfo.InvariantCulture, $"/api/exchange-rates/usd/{Day:yyyy-MM-dd}");
        public void Dispose() { Factory.Dispose(); http.Dispose(); }
    }

    private sealed class Actor(HttpClient client, Guid id, string token) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Id { get; } = id;
        public string Token { get; set; } = token;
        public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, string? token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (body is not null) { request.Content = JsonContent.Create(body, options: JsonOptions); }
            string csrf = token ?? Token;
            if (csrf.Length > 0) { request.Headers.Add("X-CSRF-TOKEN", csrf); }
            return await Client.SendAsync(request);
        }
        public void Dispose() => Client.Dispose();
    }
}