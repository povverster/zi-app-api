using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Application.Accounts;
using ZiApp.Application.Portfolios;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class PortfolioTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "")]
    [InlineData("PUT", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/archive")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/restore")]
    public async Task AllPortfolioEndpointsRequireAuthentication(string method, string suffix)
    {
        using HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/portfolios" + suffix)
        {
            Content = JsonContent.Create(new { name = "Private" }),
        };
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task OwnerCanCreateReadAndRenameWithoutOverridingOwnershipOrCurrency()
    {
        using Actor owner = await CreateActorAsync();
        Guid forgedId = Guid.CreateVersion7();
        Guid forgedOwnerId = Guid.CreateVersion7();

        using HttpResponseMessage created = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new
        {
            name = "  Довгострокові інвестиції  ",
            id = forgedId,
            ownerAccountId = forgedOwnerId,
            baseCurrencyCode = "EUR",
            isArchived = true,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        PortfolioDetails portfolio = await ReadAsync<PortfolioDetails>(created);
        Assert.Equal(7, portfolio.Id.Version);
        Assert.NotEqual(forgedId, portfolio.Id);
        Assert.Equal("Довгострокові інвестиції", portfolio.Name);
        Assert.Equal("USD", portfolio.BaseCurrencyCode);
        Assert.False(portfolio.IsArchived);
        Assert.NotEqual(default, portfolio.CreatedAtUtc);
        Assert.NotNull(created.Headers.Location);
        Assert.EndsWith($"/api/portfolios/{portfolio.Id}", created.Headers.Location.ToString(), StringComparison.Ordinal);

        using HttpResponseMessage fetched = await owner.Client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(portfolio, await ReadAsync<PortfolioDetails>(fetched));
        Assert.True(fetched.Headers.CacheControl?.NoStore);

        using HttpResponseMessage renamed = await owner.SendAsync(HttpMethod.Put, $"/api/portfolios/{portfolio.Id}", new
        {
            name = "  Retirement  ",
            ownerAccountId = forgedOwnerId,
            baseCurrencyCode = "EUR",
            isArchived = true,
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        PortfolioDetails updated = await ReadAsync<PortfolioDetails>(renamed);
        Assert.Equal("Retirement", updated.Name);
        Assert.Equal(portfolio.Id, updated.Id);
        Assert.Equal(portfolio.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal("USD", updated.BaseCurrencyCode);
        Assert.False(updated.IsArchived);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Portfolios.AsNoTracking().SingleAsync(item => item.Id == portfolio.Id);
        Assert.Equal(owner.AccountId, stored.OwnerAccountId);
        Assert.Equal("Retirement", stored.Name);
    }

    [Fact]
    public async Task ListUsesStablePaginationAndExcludesArchivedPortfoliosByDefault()
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails first = await CreatePortfolioAsync(owner, "First");
        PortfolioDetails second = await CreatePortfolioAsync(owner, "Second");
        PortfolioDetails third = await CreatePortfolioAsync(owner, "Third");

        using (HttpResponseMessage archived = await owner.SendAsync(HttpMethod.Post, $"/api/portfolios/{second.Id}/archive"))
        {
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        }

        using HttpResponseMessage activeResponse = await owner.Client.GetAsync("/api/portfolios");
        PortfolioPage active = await ReadAsync<PortfolioPage>(activeResponse);
        Assert.Equal(2, active.TotalCount);
        Assert.Equal(new[] { first.Id, third.Id }, active.Items.Select(item => item.Id));
        Assert.All(active.Items, item => Assert.False(item.IsArchived));

        using HttpResponseMessage pageOneResponse = await owner.Client.GetAsync("/api/portfolios?includeArchived=true&pageSize=2");
        PortfolioPage pageOne = await ReadAsync<PortfolioPage>(pageOneResponse);
        Assert.Equal(3, pageOne.TotalCount);
        Assert.Equal(1, pageOne.Page);
        Assert.Equal(2, pageOne.PageSize);
        Assert.Equal(new[] { first.Id, second.Id }, pageOne.Items.Select(item => item.Id));
        Assert.True(pageOne.Items[1].IsArchived);

        using HttpResponseMessage pageTwoResponse = await owner.Client.GetAsync("/api/portfolios?includeArchived=true&pageSize=2&page=2");
        PortfolioPage pageTwo = await ReadAsync<PortfolioPage>(pageTwoResponse);
        Assert.Equal(3, pageTwo.TotalCount);
        Assert.Equal(third.Id, Assert.Single(pageTwo.Items).Id);

        using HttpResponseMessage beyondResponse = await owner.Client.GetAsync("/api/portfolios?page=20");
        Assert.Empty((await ReadAsync<PortfolioPage>(beyondResponse)).Items);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task OtherAccountsCannotReadRenameArchiveOrRestorePrivatePortfolios(AccountRole otherRole)
    {
        using Actor owner = await CreateActorAsync();
        using Actor other = await CreateActorAsync(otherRole);
        PortfolioDetails portfolio = await CreatePortfolioAsync(owner, "Private");
        string path = $"/api/portfolios/{portfolio.Id}";

        using HttpResponseMessage listed = await other.Client.GetAsync("/api/portfolios?includeArchived=true");
        PortfolioPage list = await ReadAsync<PortfolioPage>(listed);
        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);

        using HttpResponseMessage read = await other.Client.GetAsync(path);
        using HttpResponseMessage renamed = await other.SendAsync(HttpMethod.Put, path, new { name = "Stolen" });
        using HttpResponseMessage archived = await other.SendAsync(HttpMethod.Post, path + "/archive");
        using HttpResponseMessage restored = await other.SendAsync(HttpMethod.Post, path + "/restore");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restored.StatusCode);

        using HttpResponseMessage original = await owner.Client.GetAsync(path);
        Assert.Equal(portfolio, await ReadAsync<PortfolioDetails>(original));
        PortfolioDetails otherPortfolio = await CreatePortfolioAsync(other, "Private");
        Assert.NotEqual(portfolio.Id, otherPortfolio.Id);
    }

    [Fact]
    public async Task MissingPortfoliosReturnNotFoundForEveryResourceOperation()
    {
        using Actor owner = await CreateActorAsync();
        string path = $"/api/portfolios/{Guid.CreateVersion7()}";
        using HttpResponseMessage read = await owner.Client.GetAsync(path);
        using HttpResponseMessage rename = await owner.SendAsync(HttpMethod.Put, path, new { name = "Missing" });
        using HttpResponseMessage archive = await owner.SendAsync(HttpMethod.Post, path + "/archive");
        using HttpResponseMessage restore = await owner.SendAsync(HttpMethod.Post, path + "/restore");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    [Theory]
    [InlineData("POST", "", false)]
    [InlineData("PUT", "/{id}", false)]
    [InlineData("POST", "/{id}/archive", false)]
    [InlineData("POST", "/{id}/restore", false)]
    [InlineData("POST", "", true)]
    [InlineData("PUT", "/{id}", true)]
    [InlineData("POST", "/{id}/archive", true)]
    [InlineData("POST", "/{id}/restore", true)]
    public async Task EveryMutationRejectsMissingOrInvalidCsrf(string method, string suffix, bool invalidToken)
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails portfolio = await CreatePortfolioAsync(owner, "Original");
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            "/api/portfolios" + suffix.Replace("{id}", portfolio.Id.ToString(), StringComparison.Ordinal))
        {
            Content = JsonContent.Create(new { name = "Changed" }),
        };
        if (invalidToken)
        {
            request.Headers.Add("X-CSRF-TOKEN", "invalid-token");
        }

        using HttpResponseMessage response = await owner.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using HttpResponseMessage originalResponse = await owner.Client.GetAsync($"/api/portfolios/{portfolio.Id}");
        Assert.Equal(portfolio, await ReadAsync<PortfolioDetails>(originalResponse));
        using HttpResponseMessage listResponse = await owner.Client.GetAsync("/api/portfolios?includeArchived=true");
        Assert.Equal(1, (await ReadAsync<PortfolioPage>(listResponse)).TotalCount);
    }

    [Fact]
    public async Task DuplicateCreateAndRenameReturnConflictAndPreserveExistingValues()
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails first = await CreatePortfolioAsync(owner, "Main");
        PortfolioDetails second = await CreatePortfolioAsync(owner, "Second");

        using HttpResponseMessage duplicate = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name = " Main " });
        using HttpResponseMessage rename = await owner.SendAsync(HttpMethod.Put, $"/api/portfolios/{second.Id}", new { name = "Main" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        string error = await duplicate.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql", error, StringComparison.Ordinal);
        Assert.DoesNotContain("ux_portfolios", error, StringComparison.Ordinal);

        using HttpResponseMessage secondResponse = await owner.Client.GetAsync($"/api/portfolios/{second.Id}");
        Assert.Equal("Second", (await ReadAsync<PortfolioDetails>(secondResponse)).Name);
        using HttpResponseMessage unchanged = await owner.SendAsync(HttpMethod.Put, $"/api/portfolios/{first.Id}", new { name = "Main" });
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);

        // Preserve the existing database's case-sensitive naming semantics.
        PortfolioDetails differentCase = await CreatePortfolioAsync(owner, "main");
        Assert.NotEqual(first.Id, differentCase.Id);
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesProduceOnePortfolioAndOneConflict()
    {
        using Actor owner = await CreateActorAsync();
        Task<HttpResponseMessage> first = owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name = "Concurrent" });
        Task<HttpResponseMessage> second = owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name = "Concurrent" });
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        using HttpResponseMessage listed = await owner.Client.GetAsync("/api/portfolios");
        Assert.Equal(1, (await ReadAsync<PortfolioPage>(listed)).TotalCount);
    }

    [Fact]
    public async Task NamesAreValidatedOnCreateAndRename()
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails portfolio = await CreatePortfolioAsync(owner, "Original");
        string?[] invalidNames = [null, "", "   ", new string('x', 201)];

        foreach (string? name in invalidNames)
        {
            using HttpResponseMessage create = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name });
            using HttpResponseMessage rename = await owner.SendAsync(HttpMethod.Put, $"/api/portfolios/{portfolio.Id}", new { name });
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        }

        using HttpResponseMessage missingName = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
        using HttpResponseMessage original = await owner.Client.GetAsync($"/api/portfolios/{portfolio.Id}");
        Assert.Equal("Original", (await ReadAsync<PortfolioDetails>(original)).Name);
        Assert.Equal(200, (await CreatePortfolioAsync(owner, new string('x', 200))).Name.Length);
    }

    [Fact]
    public async Task InvalidPaginationIsRejectedIncludingIntegerOverflow()
    {
        using Actor owner = await CreateActorAsync();
        string[] queries =
        [
            "?page=0",
            "?page=-1",
            "?pageSize=0",
            "?pageSize=101",
            "?page=2147483647&pageSize=100",
            "?includeArchived=invalid",
        ];
        foreach (string query in queries)
        {
            using HttpResponseMessage response = await owner.Client.GetAsync("/api/portfolios" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task ArchivingAndRestoringPopulatedPortfolioPreservesTradesAndReportSnapshots()
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails portfolio = await CreatePortfolioAsync(owner, "History");
        (Guid tradeId, Guid runId, Guid matchId) = await SeedHistoryAsync(portfolio.Id);
        string path = $"/api/portfolios/{portfolio.Id}";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage archive = await owner.SendAsync(HttpMethod.Post, path + "/archive");
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
            Assert.True((await ReadAsync<PortfolioDetails>(archive)).IsArchived);
        }

        using (HttpResponseMessage active = await owner.Client.GetAsync("/api/portfolios"))
        {
            Assert.Empty((await ReadAsync<PortfolioPage>(active)).Items);
        }
        using (HttpResponseMessage archived = await owner.Client.GetAsync(path))
        {
            Assert.True((await ReadAsync<PortfolioDetails>(archived)).IsArchived);
        }
        using (HttpResponseMessage reservedName = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name = "History" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, reservedName.StatusCode);
        }
        using (HttpResponseMessage rename = await owner.SendAsync(HttpMethod.Put, path, new { name = "Archived history" }))
        {
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
            Assert.True((await ReadAsync<PortfolioDetails>(rename)).IsArchived);
        }
        using (HttpResponseMessage deleted = await owner.SendAsync(HttpMethod.Delete, path))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, deleted.StatusCode);
        }

        await AssertHistoryAsync(portfolio.Id, tradeId, runId, matchId);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage restore = await owner.SendAsync(HttpMethod.Post, path + "/restore");
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            Assert.False((await ReadAsync<PortfolioDetails>(restore)).IsArchived);
        }

        using HttpResponseMessage restoredList = await owner.Client.GetAsync("/api/portfolios");
        Assert.Equal(portfolio.Id, Assert.Single((await ReadAsync<PortfolioPage>(restoredList)).Items).Id);
        await AssertHistoryAsync(portfolio.Id, tradeId, runId, matchId);
    }

    [Fact]
    public async Task DeactivatedAccountCannotUseAnExistingSessionForPortfolioOperations()
    {
        using Actor owner = await CreateActorAsync();
        PortfolioDetails portfolio = await CreatePortfolioAsync(owner, "Before deactivation");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            UserAccount account = await dbContext.UserAccounts.SingleAsync(item => item.Id == owner.AccountId);
            account.Deactivate();
            await dbContext.SaveChangesAsync();
        }

        string path = $"/api/portfolios/{portfolio.Id}";
        using HttpResponseMessage list = await owner.Client.GetAsync("/api/portfolios");
        using HttpResponseMessage get = await owner.Client.GetAsync(path);
        using HttpResponseMessage create = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name = "New" });
        using HttpResponseMessage rename = await owner.SendAsync(HttpMethod.Put, path, new { name = "Changed" });
        using HttpResponseMessage archive = await owner.SendAsync(HttpMethod.Post, path + "/archive");
        using HttpResponseMessage restore = await owner.SendAsync(HttpMethod.Post, path + "/restore");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, archive.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, restore.StatusCode);
    }

    [Fact]
    public async Task SwaggerDescribesEveryPortfolioEndpoint()
    {
        using HttpResponseMessage response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/portfolios").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/portfolios").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/portfolios/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/portfolios/{id}").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/portfolios/{id}/archive").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/portfolios/{id}/restore").TryGetProperty("post", out _));
    }

    private async Task<Actor> CreateActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Portfolio!Testing123";
        string email = $"portfolio-{Guid.CreateVersion7():N}@example.test";
        Guid accountId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
            AccountProvisioningResult result = await provisioning.ProvisionAsync(new ProvisionAccountCommand(
                email, "Portfolio Tester", password, role, SupportedLanguage.English));
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            accountId = Assert.IsType<ProvisionedAccount>(result.Account).Id;
        }

        HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        try
        {
            string token = await GetTokenAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email, password }),
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return new Actor(client, accountId, await GetTokenAsync(client));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(document.RootElement.GetProperty("token").GetString());
    }

    private static async Task<PortfolioDetails> CreatePortfolioAsync(Actor owner, string name)
    {
        using HttpResponseMessage response = await owner.SendAsync(HttpMethod.Post, "/api/portfolios", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PortfolioDetails>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
    }

    private async Task<(Guid TradeId, Guid RunId, Guid MatchId)> SeedHistoryAsync(Guid portfolioId)
    {
        Guid instrumentId = Guid.CreateVersion7();
        Guid rateId = Guid.CreateVersion7();
        Guid buyId = Guid.CreateVersion7();
        Guid sellId = Guid.CreateVersion7();
        Guid runId = Guid.CreateVersion7();
        Guid matchId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var instrument = new Instrument(instrumentId, $"T{instrumentId:N}"[..12], "NASDAQ", "Test ETF", InstrumentType.Etf, "USD");
        var rate = new ExchangeRate(rateId, "USD", DateOnly.FromDateTime(now.UtcDateTime), 40m, $"Test-{rateId:N}", now);
        var buy = new InvestmentTransaction(buyId, portfolioId, instrumentId, rateId, TradeSide.Buy, now.AddDays(-1), 1m, 100m, 1m);
        var sell = new InvestmentTransaction(sellId, portfolioId, instrumentId, rateId, TradeSide.Sell, now, 1m, 110m, 1m);
        var run = new TaxCalculationRun(runId, portfolioId, now.Year, "fifo-uah-v1", now);
        var calculation = new RealizedTaxLotMatch(buyId.ToString(), sellId.ToString(), 1m, 100m, 4000m, 110m, 4400m, 1m, 40m, 1m, 40m);
        var match = new TaxLotMatchSnapshot(matchId, runId, buyId, sellId, calculation);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.AddRange(instrument, rate, buy, sell, run, match);
        await dbContext.SaveChangesAsync();
        return (buyId, runId, matchId);
    }

    private async Task AssertHistoryAsync(Guid portfolioId, Guid tradeId, Guid runId, Guid matchId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.InvestmentTransactions.CountAsync(item => item.PortfolioId == portfolioId));
        InvestmentTransaction trade = await dbContext.InvestmentTransactions.SingleAsync(item => item.Id == tradeId);
        Assert.Equal(1m, trade.Quantity);
        Assert.Equal(100m, trade.UnitPriceUsd);
        Assert.Equal(portfolioId, (await dbContext.TaxCalculationRuns.SingleAsync(item => item.Id == runId)).PortfolioId);
        TaxLotMatchSnapshot match = await dbContext.TaxLotMatchSnapshots.SingleAsync(item => item.Id == matchId);
        Assert.Equal(4000m, match.PurchaseCostUah);
        Assert.Equal(320m, match.ProfitUah);
    }

    private sealed class Actor(HttpClient client, Guid accountId, string csrfToken) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public Guid AccountId { get; } = accountId;

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
            return await Client.SendAsync(request);
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}