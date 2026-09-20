using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Application.Accounts;
using ZiApp.Application.Portfolios;
using ZiApp.Application.Trading;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class TradingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData("GET", "/api/instruments")]
    [InlineData("POST", "/api/instruments")]
    [InlineData("GET", "/api/instruments/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades")]
    [InlineData("POST", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades")]
    [InlineData("GET", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades/corrections")]
    [InlineData("GET", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades/00000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/api/portfolios/00000000-0000-0000-0000-000000000001/trades/00000000-0000-0000-0000-000000000002/corrections")]
    public async Task AllEndpointsRequireAuthentication(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CatalogIsSharedSearchableAndOnlyAdminsCanAdd()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        using Actor user = await ActorAsync();
        string symbol = "C" + Guid.CreateVersion7().ToString("N")[..20].ToUpperInvariant();
        var input = new InstrumentInput(" " + symbol.ToLowerInvariant() + " ", " nasdaq ", "Catalog Test", InstrumentType.Etf);
        using var forbidden = await user.SendAsync("/api/instruments", input);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var created = await admin.SendAsync("/api/instruments", input);
        var instrument = await ReadAsync<InstrumentDetails>(created, HttpStatusCode.Created);
        Assert.Equal(symbol, instrument.Symbol);
        Assert.Equal("NASDAQ", instrument.ExchangeCode);
        Assert.Equal("USD", instrument.CurrencyCode);
        Assert.Equal(7, instrument.Id.Version);
        using var get = await user.Client.GetAsync(created.Headers.Location);
        Assert.Equal(instrument, await ReadAsync<InstrumentDetails>(get));
        using var list = await user.Client.GetAsync($"/api/instruments?search={symbol.ToLowerInvariant()}&pageSize=1");
        Assert.Equal(instrument, Assert.Single((await ReadAsync<TradingPage<InstrumentDetails>>(list)).Items));
        using var duplicate = await admin.SendAsync("/api/instruments", input);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var wildcard = await user.Client.GetAsync("/api/instruments?search=%25");
        Assert.Empty((await ReadAsync<TradingPage<InstrumentDetails>>(wildcard)).Items);
        using var missing = await user.Client.GetAsync($"/api/instruments/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TradesRoundTripExactAmountsAndServerOwnedFields()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        using var created = await owner.SendAsync(Path(portfolioId), new
        {
            instrumentId,
            side = "Buy",
            executedAt = "2025-01-01T14:00:00.123456+02:00",
            quantity = "123.123456789012",
            unitPriceUsd = "12.123456789012",
            feeUsd = "0.000000000001",
            brokerTransactionId = " broker-1 ",
            ownerAccountId = Guid.CreateVersion7(),
            id = Guid.CreateVersion7(),
            exchangeRateId = Guid.CreateVersion7(),
            isSuperseded = true,
            fifoOrderId = Guid.CreateVersion7(),
        });
        var trade = await ReadAsync<TradeDetails>(created, HttpStatusCode.Created);
        Assert.Equal(7, trade.Id.Version);
        Assert.Equal(trade.Id, trade.FifoOrderId);
        Assert.Equal("123.123456789012", trade.Quantity);
        Assert.Equal("12.123456789012", trade.UnitPriceUsd);
        Assert.Equal("0.000000000001", trade.FeeUsd);
        Assert.Equal("broker-1", trade.BrokerTransactionId);
        Assert.Equal(DateTimeOffset.Parse("2025-01-01T12:00:00.123456Z", System.Globalization.CultureInfo.InvariantCulture), trade.ExecutedAtUtc);
        Assert.Equal("2025-01-01T14:00:00.123456+02:00", trade.ExecutedAtOriginal);
        Assert.Null(trade.ExchangeRateId);
        Assert.Equal("Pending", trade.RateStatus);
        Assert.False(trade.IsTaxReady);
        Assert.False(trade.IsSuperseded);
        using var get = await owner.Client.GetAsync(created.Headers.Location);
        Assert.Equal(trade, await ReadAsync<TradeDetails>(get));
        Assert.True(get.Headers.CacheControl?.NoStore);
        using var sale = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, "0.5", day: 2));
        await ReadAsync<TradeDetails>(sale, HttpStatusCode.Created);
        using var list = await owner.Client.GetAsync(Path(portfolioId) + "?page=2&pageSize=1");
        var page = await ReadAsync<TradingPage<TradeDetails>>(list);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(TradeSide.Sell, Assert.Single(page.Items).Side);
        using var empty = await owner.Client.GetAsync(Path(portfolioId) + "?page=3&pageSize=1");
        Assert.Empty((await ReadAsync<TradingPage<TradeDetails>>(empty)).Items);
        using var delete = await owner.Client.DeleteAsync(Path(portfolioId) + $"/{trade.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task OtherAccountsCannotReadCreateOrCorrectTrades(AccountRole role)
    {
        using Actor owner = await ActorAsync();
        using Actor stranger = await ActorAsync(role);
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeDetails trade = await BuyAsync(owner, portfolioId, instrumentId);
        foreach (string path in new[] { Path(portfolioId), Path(portfolioId) + $"/{trade.Id}", Path(portfolioId) + "/corrections" })
        {
            using var response = await stranger.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var create = await stranger.SendAsync(Path(portfolioId), Input(instrumentId));
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        using var correct = await stranger.SendAsync(Path(portfolioId) + $"/{trade.Id}/corrections",
            new CorrectTradeInput(Input(instrumentId), "Not mine"));
        Assert.Equal(HttpStatusCode.NotFound, correct.StatusCode);
        Guid strangerPortfolio = await PortfolioAsync(stranger);
        using var crossPortfolio = await stranger.SendAsync(Path(strangerPortfolio) + $"/{trade.Id}/corrections",
            new CorrectTradeInput(Input(instrumentId), "Wrong portfolio"));
        Assert.Equal(HttpStatusCode.NotFound, crossPortfolio.StatusCode);
    }

    [Fact]
    public async Task BrokerDuplicatesAreTrimmedCaseSensitiveAndScopedToPortfolio()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        await BuyAsync(owner, portfolioId, instrumentId, brokerId: "B1");
        using var duplicate = await owner.SendAsync(Path(portfolioId), Input(instrumentId) with { BrokerTransactionId = " B1 " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await BuyAsync(owner, portfolioId, instrumentId, brokerId: "b1");
        await BuyAsync(owner, await PortfolioAsync(owner), instrumentId, brokerId: "B1");
        await BuyAsync(owner, portfolioId, instrumentId);
        await BuyAsync(owner, portfolioId, instrumentId);
    }

    [Fact]
    public async Task OversellingBackdatingAndConcurrentSalesAreRejected()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        await BuyAsync(owner, portfolioId, instrumentId);
        using var backdated = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell) with { ExecutedAt = "2024-12-31T12:00:00Z" });
        Assert.Equal(HttpStatusCode.Conflict, backdated.StatusCode);
        using var tooMany = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, "2", 2));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        var responses = await Task.WhenAll(
            owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, day: 2)),
            owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, day: 2)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses) { response.Dispose(); }
        }

        using var list = await owner.Client.GetAsync(Path(portfolioId));
        Assert.Equal(2, (await ReadAsync<TradingPage<TradeDetails>>(list)).TotalCount);
    }

    [Fact]
    public async Task CorrectionsPreserveInputsAuditAndOrderingAndValidateLaterSales()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeDetails buy = await BuyAsync(owner, portfolioId, instrumentId, brokerId: "FIX");
        using var sale = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, "1", 2));
        await ReadAsync<TradeDetails>(sale, HttpStatusCode.Created);
        string correctionPath = Path(portfolioId) + $"/{buy.Id}/corrections";
        using var invalid = await owner.SendAsync(correctionPath,
            new CorrectTradeInput(Input(instrumentId, quantity: "0.5"), "Would oversell later"));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var changedInstrument = await owner.SendAsync(correctionPath,
            new CorrectTradeInput(Input(await InstrumentAsync()), "Would remove earlier lot"));
        Assert.Equal(HttpStatusCode.Conflict, changedInstrument.StatusCode);
        using var corrected = await owner.SendAsync(correctionPath,
            new CorrectTradeInput(Input(instrumentId) with { UnitPriceUsd = "90", BrokerTransactionId = "FIX" }, " Correct broker price "));
        var replacement = await ReadAsync<TradeDetails>(corrected, HttpStatusCode.Created);
        Assert.NotEqual(buy.Id, replacement.Id);
        Assert.Equal(buy.FifoOrderId, replacement.FifoOrderId);
        Assert.Equal("90", replacement.UnitPriceUsd);
        using var old = await owner.Client.GetAsync(Path(portfolioId) + $"/{buy.Id}");
        Assert.Equal(buy with { IsSuperseded = true }, await ReadAsync<TradeDetails>(old));
        using var history = await owner.Client.GetAsync(Path(portfolioId) + "/corrections");
        var audit = Assert.Single((await ReadAsync<TradingPage<CorrectionDetails>>(history)).Items);
        Assert.Equal(buy.Id, audit.OriginalTradeId);
        Assert.Equal(replacement.Id, audit.ReplacementTradeId);
        Assert.Equal(owner.Id, audit.ActorAccountId);
        Assert.Equal("Correct broker price", audit.Reason);
        Assert.Equal(7, audit.Id.Version);
        using var repeat = await owner.SendAsync(correctionPath, new CorrectTradeInput(Input(instrumentId), "Retry old ID"));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        using var current = await owner.Client.GetAsync(Path(portfolioId));
        Assert.Equal(2, (await ReadAsync<TradingPage<TradeDetails>>(current)).TotalCount);
        using var all = await owner.Client.GetAsync(Path(portfolioId) + "?includeSuperseded=true");
        Assert.Equal(3, (await ReadAsync<TradingPage<TradeDetails>>(all)).TotalCount);
        using var chain = await owner.SendAsync(Path(portfolioId) + $"/{replacement.Id}/corrections",
            new CorrectTradeInput(Input(instrumentId) with { BrokerTransactionId = "FIX" }, "Second correction"));
        Assert.Equal(buy.FifoOrderId, (await ReadAsync<TradeDetails>(chain, HttpStatusCode.Created)).FifoOrderId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllMutationsRequireValidCsrf(bool invalidToken)
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        Guid portfolioId = await PortfolioAsync(admin);
        Guid instrumentId = await InstrumentAsync();
        TradeDetails buy = await BuyAsync(admin, portfolioId, instrumentId);
        foreach (var (path, body) in new (string, object)[]
        {
            ("/api/instruments", new InstrumentInput("CSRF", "TEST", "CSRF Test", InstrumentType.Stock)),
            (Path(portfolioId), Input(instrumentId)),
            (Path(portfolioId) + $"/{buy.Id}/corrections", new CorrectTradeInput(Input(instrumentId), "CSRF")),
        })
        {
            using var response = await admin.SendAsync(path, body, invalidToken ? "invalid" : "");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var list = await admin.Client.GetAsync(Path(portfolioId) + "?includeSuperseded=true");
        Assert.Single((await ReadAsync<TradingPage<TradeDetails>>(list)).Items);
    }

    [Fact]
    public async Task ArchivedAndInactiveAccountsCannotMutate()
    {
        using Actor owner = await ActorAsync(AccountRole.SuperAdmin);
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeDetails buy = await BuyAsync(owner, portfolioId, instrumentId);
        using var archive = await owner.SendAsync($"/api/portfolios/{portfolioId}/archive", new { });
        archive.EnsureSuccessStatusCode();
        using var create = await owner.SendAsync(Path(portfolioId), Input(instrumentId));
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        using var correction = await owner.SendAsync(Path(portfolioId) + $"/{buy.Id}/corrections", new CorrectTradeInput(Input(instrumentId), "Archived"));
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        using var read = await owner.Client.GetAsync(Path(portfolioId));
        Assert.Single((await ReadAsync<TradingPage<TradeDetails>>(read)).Items);
        using var restore = await owner.SendAsync($"/api/portfolios/{portfolioId}/restore", new { });
        restore.EnsureSuccessStatusCode();
        await BuyAsync(owner, portfolioId, instrumentId);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(item => item.Id == owner.Id)).Deactivate();
            await db.SaveChangesAsync();
        }

        foreach (string path in new[] { "/api/instruments", $"/api/instruments/{instrumentId}", Path(portfolioId), Path(portfolioId) + $"/{buy.Id}", Path(portfolioId) + "/corrections" })
        {
            using var response = await owner.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var disabled = await owner.SendAsync(Path(portfolioId), Input(instrumentId));
        Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
        using var disabledCorrection = await owner.SendAsync(Path(portfolioId) + $"/{buy.Id}/corrections", new CorrectTradeInput(Input(instrumentId), "Disabled"));
        Assert.Equal(HttpStatusCode.Unauthorized, disabledCorrection.StatusCode);
        using var disabledCatalog = await owner.SendAsync("/api/instruments", new InstrumentInput("OFF", "TEST", "Disabled", InstrumentType.Stock));
        Assert.Equal(HttpStatusCode.Unauthorized, disabledCatalog.StatusCode);
    }

    [Fact]
    public async Task InvalidTradeAndPaginationInputsDoNotWriteData()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeInput valid = Input(instrumentId);
        foreach (TradeInput invalid in new[]
        {
            valid with { Quantity = "0" }, valid with { Quantity = "1.0000000000001" },
            valid with { Quantity = "10000000000000000" }, valid with { FeeUsd = "-1" },
            valid with { UnitPriceUsd = "0" }, valid with { Side = (TradeSide)99 },
            valid with { ExecutedAt = "2025-01-01T12:00:00" }, valid with { ExecutedAt = "2025-01-01T12:00:00.1234567Z" },
            valid with { BrokerTransactionId = new string('x', 201) }, valid with { InstrumentId = Guid.Empty },
        })
        {
            using var response = await owner.SendAsync(Path(portfolioId), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var numeric = await owner.SendAsync(Path(portfolioId), new { instrumentId, side = "Buy", executedAt = valid.ExecutedAt, quantity = 1.0, unitPriceUsd = "1", feeUsd = "0" });
        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
        using var missingInstrument = await owner.SendAsync(Path(portfolioId), valid with { InstrumentId = Guid.CreateVersion7() });
        Assert.Equal(HttpStatusCode.NotFound, missingInstrument.StatusCode);
        foreach (string query in new[] { "page=0", "pageSize=101", "pageSize=0", "page=2147483647&pageSize=100" })
        {
            foreach (string path in new[] { "/api/instruments", Path(portfolioId), Path(portfolioId) + "/corrections" })
            {
                using var response = await owner.Client.GetAsync(path + "?" + query);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        using var list = await owner.Client.GetAsync(Path(portfolioId));
        Assert.Empty((await ReadAsync<TradingPage<TradeDetails>>(list)).Items);
    }

    [Fact]
    public async Task SwaggerDescribesTradingRoutes()
    {
        using var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/instruments", out _));
        Assert.True(paths.TryGetProperty("/api/portfolios/{portfolioId}/trades/{id}/corrections", out _));
        Assert.Equal("string", document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("TradeInput").GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesAndCorrectionsOnlyCommitOnce()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeInput input = Input(instrumentId) with { BrokerTransactionId = "RACE" };
        var creates = await Task.WhenAll(owner.SendAsync(Path(portfolioId), input), owner.SendAsync(Path(portfolioId), input));
        TradeDetails original;
        try
        {
            original = await ReadAsync<TradeDetails>(Assert.Single(creates, response => response.StatusCode == HttpStatusCode.Created), HttpStatusCode.Created);
            Assert.Single(creates, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in creates) { response.Dispose(); } }

        string path = Path(portfolioId) + $"/{original.Id}/corrections";
        var corrections = await Task.WhenAll(
            owner.SendAsync(path, new CorrectTradeInput(input with { UnitPriceUsd = "80" }, "First edit")),
            owner.SendAsync(path, new CorrectTradeInput(input with { UnitPriceUsd = "90" }, "Second edit")));
        try
        {
            Assert.Single(corrections, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(corrections, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in corrections) { response.Dispose(); } }

        using var audit = await owner.Client.GetAsync(Path(portfolioId) + "/corrections");
        Assert.Single((await ReadAsync<TradingPage<CorrectionDetails>>(audit)).Items);
    }

    [Fact]
    public async Task CorrectionsCannotStealAnotherBrokerIdAndNeedAReasonAndReplacement()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        TradeDetails first = await BuyAsync(owner, portfolioId, instrumentId, "FIRST");
        await BuyAsync(owner, portfolioId, instrumentId, "SECOND");
        string path = Path(portfolioId) + $"/{first.Id}/corrections";
        using var conflict = await owner.SendAsync(path, new CorrectTradeInput(Input(instrumentId) with { BrokerTransactionId = "SECOND" }, "Duplicate"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        foreach (object input in new object[]
        {
            new { replacement = Input(instrumentId), reason = " " },
            new { replacement = Input(instrumentId), reason = new string('x', 1001) },
            new { reason = "Missing replacement" },
            new { replacement = (TradeInput?)null, reason = "Null replacement" },
        })
        {
            using var response = await owner.SendAsync(path, input);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var get = await owner.Client.GetAsync(Path(portfolioId) + $"/{first.Id}");
        Assert.Equal(first, await ReadAsync<TradeDetails>(get));
        using var audit = await owner.Client.GetAsync(Path(portfolioId) + "/corrections");
        Assert.Empty((await ReadAsync<TradingPage<CorrectionDetails>>(audit)).Items);
    }

    [Fact]
    public async Task StoredSplitsAllowSalesWithoutInventingRates()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        await BuyAsync(owner, portfolioId, instrumentId);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.StockSplits.Add(new StockSplit(Guid.CreateVersion7(), instrumentId,
                new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero), 5m, 1m));
            await db.SaveChangesAsync();
        }

        using var sale = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, "5", 3));
        Assert.Null((await ReadAsync<TradeDetails>(sale, HttpStatusCode.Created)).ExchangeRateId);
        using var oversell = await owner.SendAsync(Path(portfolioId), Input(instrumentId, TradeSide.Sell, "0.1", 4));
        Assert.Equal(HttpStatusCode.Conflict, oversell.StatusCode);
    }

    [Fact]
    public async Task CorrectingAReportedTradePreservesSavedSourceRateAndSnapshot()
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = await PortfolioAsync(owner);
        Guid instrumentId = await InstrumentAsync();
        Guid buyId = Guid.CreateVersion7();
        Guid rateId = Guid.CreateVersion7();
        Guid matchId = Guid.CreateVersion7();
        Guid runId = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DateTimeOffset time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var buy = new InvestmentTransaction(buyId, portfolioId, instrumentId, rateId, TradeSide.Buy, time, 1m, 100m, 1m);
            var sell = new InvestmentTransaction(Guid.CreateVersion7(), portfolioId, instrumentId, rateId, TradeSide.Sell, time.AddDays(1), 1m, 110m, 1m);
            db.AddRange(new ExchangeRate(rateId, "USD", new DateOnly(2025, 1, 1), 40m, $"Test-{rateId}", time), buy, sell,
                new TaxCalculationRun(runId, portfolioId, 2025, "fifo-uah-v1", time.AddDays(2)),
                new TaxLotMatchSnapshot(matchId, runId, buy.Id, sell.Id,
                    new RealizedTaxLotMatch(buy.Id.ToString(), sell.Id.ToString(), 1m, 100m, 4000m, 110m, 4400m, 1m, 40m, 1m, 40m)));
            await db.SaveChangesAsync();
        }

        using var corrected = await owner.SendAsync(Path(portfolioId) + $"/{buyId}/corrections",
            new CorrectTradeInput(Input(instrumentId) with { UnitPriceUsd = "99" }, "Correct reported purchase"));
        var replacement = await ReadAsync<TradeDetails>(corrected, HttpStatusCode.Created);
        Assert.Null(replacement.ExchangeRateId);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await db.InvestmentTransactions.SingleAsync(item => item.Id == buyId);
            Assert.True(original.IsSuperseded);
            Assert.Equal(rateId, original.ExchangeRateId);
            Assert.Equal(100m, original.UnitPriceUsd);
            Assert.Equal(1m, original.FeeUsd);
            var match = await db.TaxLotMatchSnapshots.SingleAsync(item => item.Id == matchId);
            Assert.Equal(buyId, match.PurchaseTransactionId);
            Assert.Equal(320m, match.ProfitUah);
            Assert.True(await db.TaxCalculationRuns.AnyAsync(item => item.Id == runId));
        }
    }

    [Fact]
    public async Task CatalogValidationAndDuplicateIsinAreEnforced()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var valid = new InstrumentInput("VALID", "TEST", "Valid", InstrumentType.Stock);
        foreach (var invalid in new[]
        {
            valid with { Symbol = " " }, valid with { Symbol = new string('x', 33) },
            valid with { ExchangeCode = " " }, valid with { Name = new string('x', 301) },
            valid with { Type = (InstrumentType)99 }, valid with { Isin = "SHORT" }, valid with { Isin = "INVALID#ISIN" },
        })
        {
            using var response = await admin.SendAsync("/api/instruments", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        string isin = "US" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();
        using var first = await admin.SendAsync("/api/instruments", valid with { Symbol = "ISIN1", Isin = isin });
        await ReadAsync<InstrumentDetails>(first, HttpStatusCode.Created);
        using var duplicate = await admin.SendAsync("/api/instruments", valid with { Symbol = "ISIN2", Isin = isin.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonUsdInstrumentsOrPortfoliosAreRejected(bool nonUsdInstrument)
    {
        using Actor owner = await ActorAsync();
        Guid portfolioId = Guid.CreateVersion7();
        Guid instrumentId = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(
                new ZiApp.Domain.Portfolios.Portfolio(portfolioId, owner.Id, "Currency test", nonUsdInstrument ? "USD" : "EUR", DateTimeOffset.UtcNow),
                new Instrument(instrumentId, "FX" + instrumentId.ToString("N")[..20], "TEST", "Currency test", InstrumentType.Stock, nonUsdInstrument ? "EUR" : "USD"));
            await db.SaveChangesAsync();
        }

        using var response = await owner.SendAsync(Path(portfolioId), Input(instrumentId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UnsupportedCurrency", body.RootElement.GetProperty("code").GetString());
    }

    private async Task<Actor> ActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Trading!Testing123";
        string email = $"trade-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
            var result = await service.ProvisionAsync(new ProvisionAccountCommand(email, "Trader", password, role, SupportedLanguage.English));
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            id = Assert.IsType<ProvisionedAccount>(result.Account).Id;
        }

        HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        try
        {
            var actor = new Actor(client, id, await TokenAsync(client));
            using var login = await actor.SendAsync("/api/auth/login", new { email, password });
            login.EnsureSuccessStatusCode();
            actor.Token = await TokenAsync(client);
            return actor;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(document.RootElement.GetProperty("token").GetString());
    }

    private async Task<Guid> InstrumentAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = new Instrument(Guid.CreateVersion7(), "T" + Guid.CreateVersion7().ToString("N")[..20], "TEST", "Test stock", InstrumentType.Stock, "USD");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();
        return instrument.Id;
    }

    private static async Task<Guid> PortfolioAsync(Actor actor)
    {
        using var response = await actor.SendAsync("/api/portfolios", new { name = Guid.CreateVersion7().ToString() });
        return (await ReadAsync<PortfolioDetails>(response, HttpStatusCode.Created)).Id;
    }

    private static async Task<TradeDetails> BuyAsync(Actor actor, Guid portfolioId, Guid instrumentId, string? brokerId = null)
    {
        using var response = await actor.SendAsync(Path(portfolioId), Input(instrumentId) with { BrokerTransactionId = brokerId });
        return await ReadAsync<TradeDetails>(response, HttpStatusCode.Created);
    }

    private static TradeInput Input(Guid instrumentId, TradeSide side = TradeSide.Buy, string quantity = "1", int day = 1) =>
        new(instrumentId, side, $"2025-01-{day:D2}T12:00:00Z", quantity, "100.25", "0.5");

    private static string Path(Guid portfolioId) => $"/api/portfolios/{portfolioId}/trades";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}; received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(JsonOptions));
    }

    private sealed class Actor(HttpClient client, Guid id, string token) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Id { get; } = id;
        public string Token { get; set; } = token;
        public async Task<HttpResponseMessage> SendAsync(string path, object body, string? csrf = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
            string selected = csrf ?? Token;
            if (selected.Length > 0) { request.Headers.Add("X-CSRF-TOKEN", selected); }
            return await Client.SendAsync(request);
        }

        public void Dispose() => Client.Dispose();
    }
}