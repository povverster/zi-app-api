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
using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class HoldingsAndSplitTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset Time = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("GET", "/api/portfolios/00000000-0000-0000-0000-000000000001/holdings")]
    [InlineData("GET", "/api/instruments/00000000-0000-0000-0000-000000000001/splits")]
    [InlineData("POST", "/api/instruments/00000000-0000-0000-0000-000000000001/splits")]
    [InlineData("POST", "/api/instruments/00000000-0000-0000-0000-000000000001/splits/00000000-0000-0000-0000-000000000002/corrections")]
    public async Task AnonymousRequestsAreRejected(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HoldingsExposeRemainingLotsIndependentFxAndHistoricalCutoffs()
    {
        using Actor owner = await ActorAsync();
        var seed = await SeedAsync(owner, buyQuantity: 3m, saleQuantity: 1m);
        using var response = await owner.Client.GetAsync(Holdings(seed));
        var result = await ReadAsync<HoldingsDetails>(response);
        Assert.True(result.IsComplete);
        Assert.False(result.IsTaxReady);
        Assert.Equal(FifoRealizedGainCalculator.HoldingsVersion, result.CalculationVersion);
        var position = Assert.Single(result.Positions);
        Assert.Equal("2", position.Quantity);
        var lot = Assert.Single(position.OpenLots!);
        Assert.Equal("200", lot.PurchaseCostUsd);
        Assert.Equal("8000", lot.PurchaseCostUah);
        Assert.Equal("2", lot.PurchaseFeeUsd);
        var match = Assert.Single(position.RealizedMatches!);
        Assert.Equal("18", match.ProfitUsd);
        Assert.Equal("839", match.ProfitUah);
        Assert.Equal(seed.BuyId, match.PurchaseTradeId);
        Assert.Equal(seed.SaleId, match.SaleTradeId);
        Assert.Equal("18", result.RealizedTotals!.ProfitUsd);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        using var history = await owner.Client.GetAsync(Holdings(seed) + "?asOf=2025-01-01T14%3A00%3A00%2B02%3A00");
        var earlier = await ReadAsync<HoldingsDetails>(history);
        Assert.Equal("3", Assert.Single(earlier.Positions).Quantity);
        Assert.Empty(earlier.Positions[0].RealizedMatches!);
        using var before = await owner.Client.GetAsync(Holdings(seed) + "?asOf=2024-12-31T00:00:00Z");
        Assert.Empty((await ReadAsync<HoldingsDetails>(before)).Positions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresolvedOrLegacyRatesNeverProducePartialFinancialTotals(bool legacy)
    {
        using Actor owner = await ActorAsync();
        var seed = await SeedAsync(owner, verified: false, legacy: legacy);
        using var response = await owner.Client.GetAsync(Holdings(seed));
        var result = await ReadAsync<HoldingsDetails>(response);
        var position = Assert.Single(result.Positions);
        Assert.Equal("3", position.Quantity);
        Assert.Equal("PendingRates", position.Status);
        Assert.False(result.IsComplete);
        Assert.Null(result.RealizedTotals);
        Assert.Null(position.RealizedMatches);
        Assert.Null(position.OpenLots);
        Assert.Equal(legacy ? "LinkedUnverified" : "Pending", Assert.Single(position.RateBlockers).Status);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InvestmentTransactions.SingleAsync(t => t.Id == seed.BuyId)).ExchangeRatePolicy);
    }

    [Theory]
    [InlineData(AccountRole.User)]
    [InlineData(AccountRole.SuperAdmin)]
    public async Task HoldingsRemainPrivateEvenToAnotherAdministrator(AccountRole role)
    {
        using Actor owner = await ActorAsync();
        using Actor stranger = await ActorAsync(role);
        var seed = await SeedAsync(owner);
        using var response = await stranger.Client.GetAsync(Holdings(seed));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var missing = await stranger.Client.GetAsync($"/api/portfolios/{Guid.CreateVersion7()}/holdings");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SharedSplitsAreAdminOnlyAndMutationsRequireCsrf()
    {
        using Actor user = await ActorAsync();
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(user);
        using var denied = await user.PostAsync(Splits(seed), Input());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var csrf = await admin.PostAsync(Splits(seed), Input(), "");
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var created = await admin.PostAsync(Splits(seed), Input());
        var split = await ReadAsync<SplitDetails>(created, HttpStatusCode.Created);
        Assert.Equal(admin.Id, split.RecordedByAccountId);
        Assert.Equal(split.Id, split.FifoOrderId);
        Assert.Equal(7, split.Id.Version);
        Assert.Equal(Input().EffectiveAt, split.EffectiveAtOriginal);
        Assert.Equal("Broker corporate-action notice", split.SourceReference);
        using var read = await user.Client.GetAsync(created.Headers.Location);
        Assert.Equal(split, await ReadAsync<SplitDetails>(read));
        using var correctionDenied = await user.PostAsync(Splits(seed) + $"/{split.Id}/corrections", new CorrectSplitInput(Input(), "Fix"));
        Assert.Equal(HttpStatusCode.Forbidden, correctionDenied.StatusCode);
        using var correctionCsrf = await admin.PostAsync(Splits(seed) + $"/{split.Id}/corrections", new CorrectSplitInput(Input(), "Fix"), "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, correctionCsrf.StatusCode);
    }

    [Fact]
    public async Task DisabledAdministratorCannotReadOrWrite()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserAccounts.SingleAsync(a => a.Id == admin.Id)).Deactivate();
            await db.SaveChangesAsync();
        }
        using var read = await admin.Client.GetAsync(Splits(seed));
        using var holdings = await admin.Client.GetAsync(Holdings(seed));
        using var write = await admin.PostAsync(Splits(seed), Input());
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, holdings.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task SplitCorrectionsPreserveOriginalInputsOrderingAndSavedReports()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin, buyQuantity: 3m, saleQuantity: 1m);
        Guid snapshotId = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = new TaxCalculationRun(Guid.CreateVersion7(), seed.PortfolioId, 2025, "fifo-uah-v1", Time.AddMonths(1));
            db.AddRange(run, new TaxLotMatchSnapshot(snapshotId, run.Id, seed.BuyId, seed.SaleId!.Value,
                new RealizedTaxLotMatch(seed.BuyId.ToString(), seed.SaleId.ToString()!, 1, 100, 4000, 120, 4920, 1, 40, 1, 41)));
            await db.SaveChangesAsync();
        }
        using var created = await admin.PostAsync(Splits(seed), Input());
        var original = await ReadAsync<SplitDetails>(created, HttpStatusCode.Created);
        using var projected = await admin.Client.GetAsync(Holdings(seed));
        Assert.Equal("5", Assert.Single((await ReadAsync<HoldingsDetails>(projected)).Positions).Quantity);
        using var corrected = await admin.PostAsync(Splits(seed) + $"/{original.Id}/corrections",
            new CorrectSplitInput(Input() with { Numerator = "3" }, "Corrected official ratio"));
        var replacement = await ReadAsync<SplitDetails>(corrected, HttpStatusCode.Created);
        Assert.Equal(original.FifoOrderId, replacement.FifoOrderId);
        Assert.Equal(original.Id, replacement.PreviousSplitId);
        Assert.Equal("Corrected official ratio", replacement.CorrectionReason);
        using var oldRead = await admin.Client.GetAsync(Splits(seed) + $"/{original.Id}");
        var old = await ReadAsync<SplitDetails>(oldRead);
        Assert.True(old.IsSuperseded);
        Assert.Equal("2", old.Numerator);
        using var current = await admin.Client.GetAsync(Holdings(seed));
        Assert.Equal("8", Assert.Single((await ReadAsync<HoldingsDetails>(current)).Positions).Quantity);
        using var list = await admin.Client.GetAsync(Splits(seed));
        Assert.Equal(replacement.Id, Assert.Single((await ReadAsync<TradingPage<SplitDetails>>(list)).Items).Id);
        using var all = await admin.Client.GetAsync(Splits(seed) + "?includeSuperseded=true&pageSize=1");
        var history = await ReadAsync<TradingPage<SplitDetails>>(all);
        Assert.Equal(2, history.TotalCount);
        Assert.Single(history.Items);
        using var retry = await admin.PostAsync(Splits(seed) + $"/{original.Id}/corrections", new CorrectSplitInput(Input(), "Retry"));
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        await using var verify = fixture.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(839m, (await context.TaxLotMatchSnapshots.SingleAsync(m => m.Id == snapshotId)).ProfitUah);
        Assert.Equal(3m, (await context.InvestmentTransactions.SingleAsync(t => t.Id == seed.BuyId)).Quantity);
    }

    [Fact]
    public async Task ReverseSplitValidatesAllOwnersIncludingArchivedPortfolios()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        using Actor other = await ActorAsync();
        var seed = await SeedAsync(other, buyQuantity: 3m, saleQuantity: 2m);
        using var archive = await other.PostAsync($"/api/portfolios/{seed.PortfolioId}/archive", new { });
        archive.EnsureSuccessStatusCode();
        using var rejected = await admin.PostAsync(Splits(seed), Input() with { Numerator = "1", Denominator = "3" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.DoesNotContain(seed.PortfolioId.ToString(), await rejected.Content.ReadAsStringAsync());
        using var forward = await admin.PostAsync(Splits(seed), Input());
        await ReadAsync<SplitDetails>(forward, HttpStatusCode.Created);
        using var read = await other.Client.GetAsync(Holdings(seed));
        Assert.Equal("4", Assert.Single((await ReadAsync<HoldingsDetails>(read)).Positions).Quantity);
    }

    [Fact]
    public async Task TradeCorrectionRecalculatesCurrentHistoryAndIgnoresSupersededBuy()
    {
        using Actor owner = await ActorAsync();
        var seed = await SeedAsync(owner, buyQuantity: 3m, saleQuantity: 1m);
        using var corrected = await owner.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades/{seed.BuyId}/corrections",
            new CorrectTradeInput(new(seed.InstrumentId, TradeSide.Buy, "2025-01-01T12:00:00Z", "3", "90", "3"), "Price correction"));
        var replacement = await ReadAsync<TradeDetails>(corrected, HttpStatusCode.Created);
        using var pending = await owner.Client.GetAsync(Holdings(seed));
        Assert.False((await ReadAsync<HoldingsDetails>(pending)).IsComplete);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await db.InvestmentTransactions.Include(t => t.ExchangeRate).SingleAsync(t => t.Id == seed.BuyId);
            var trade = await db.InvestmentTransactions.SingleAsync(t => t.Id == replacement.Id);
            trade.ResolveExchangeRate(original.ExchangeRate!, new(2025, 1, 1), TradeRatePolicy.Version, owner.Id, Time.AddMonths(1));
            await db.SaveChangesAsync();
        }
        using var response = await owner.Client.GetAsync(Holdings(seed));
        var position = Assert.Single((await ReadAsync<HoldingsDetails>(response)).Positions);
        Assert.Equal("2", position.Quantity);
        Assert.Equal(replacement.Id, Assert.Single(position.RealizedMatches!).PurchaseTradeId);
        Assert.Equal("28", position.RealizedTotals!.ProfitUsd);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "1")]
    [InlineData("1.0000000000001", "1")]
    [InlineData("10000000000000000", "1")]
    [InlineData("1e2", "1")]
    [InlineData("1,5", "1")]
    public async Task InvalidRatiosAreRejected(string numerator, string denominator)
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        using var response = await admin.PostAsync(Splits(seed), Input() with { Numerator = numerator, Denominator = denominator });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2999-01-01T00:00:00Z")]
    [InlineData("2025-01-01")]
    [InlineData("2025-01-01T12:00:00")]
    [InlineData("2025-01-01T12:00:00.1234567Z")]
    public async Task InvalidCutoffsAndSplitTimesAreRejected(string time)
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        using var split = await admin.PostAsync(Splits(seed), Input() with { EffectiveAt = time });
        using var read = await admin.Client.GetAsync(Holdings(seed) + "?asOf=" + Uri.EscapeDataString(time));
        Assert.Equal(HttpStatusCode.BadRequest, split.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
    }

    [Fact]
    public async Task SourcesReasonsPaginationAndMissingResourcesAreValidated()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        using var source = await admin.PostAsync(Splits(seed), Input() with { SourceReference = " " });
        Assert.Equal(HttpStatusCode.BadRequest, source.StatusCode);
        using var unknown = await admin.PostAsync($"/api/instruments/{Guid.CreateVersion7()}/splits", Input());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var paging = await admin.Client.GetAsync(Splits(seed) + "?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, paging.StatusCode);
        using var created = await admin.PostAsync(Splits(seed), Input());
        var split = await ReadAsync<SplitDetails>(created, HttpStatusCode.Created);
        using var reason = await admin.PostAsync(Splits(seed) + $"/{split.Id}/corrections", new CorrectSplitInput(Input(), ""));
        Assert.Equal(HttpStatusCode.BadRequest, reason.StatusCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateSplitsCommitOnlyOnce()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        var responses = await Task.WhenAll(admin.PostAsync(Splits(seed), Input()), admin.PostAsync(Splits(seed), Input()));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) { response.Dispose(); } }
        using var read = await admin.Client.GetAsync(Holdings(seed));
        Assert.Equal("6", Assert.Single((await ReadAsync<HoldingsDetails>(read)).Positions).Quantity);
    }

    [Fact]
    public async Task ConcurrentSaleAndReverseSplitCannotTogetherOversell()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin);
        var responses = await Task.WhenAll(
            admin.PostAsync(Splits(seed), Input() with { Numerator = "1", Denominator = "3" }),
            admin.PostAsync($"/api/portfolios/{seed.PortfolioId}/trades",
                new TradeInput(seed.InstrumentId, TradeSide.Sell, "2025-01-03T12:00:00Z", "2", "120", "1")));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) { response.Dispose(); } }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(TradeQuantityValidator.CanExecute(await db.InvestmentTransactions.Where(t => t.PortfolioId == seed.PortfolioId).ToListAsync(),
            await db.StockSplits.Where(s => s.InstrumentId == seed.InstrumentId).ToListAsync()));
    }

    [Fact]
    public async Task SwaggerDescribesSuccessBodiesAndExactDecimalStrings()
    {
        using var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/portfolios/{portfolioId}/holdings", out _));
        Assert.True(paths.TryGetProperty("/api/instruments/{instrumentId}/splits/{id}/corrections", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal("string", schemas.GetProperty("SplitInput").GetProperty("properties").GetProperty("numerator").GetProperty("type").GetString());
        Assert.Equal("string", schemas.GetProperty("PositionDetails").GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString());
    }

    [Fact]
    public async Task MixedRateReadinessKeepsCompletePositionsButWithholdsPortfolioTotals()
    {
        using Actor owner = await ActorAsync();
        var seed = await SeedAsync(owner, saleQuantity: 1m);
        Guid pendingInstrument = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Instruments.Add(new(pendingInstrument, pendingInstrument.ToString("N")[..20], "TEST", "Pending stock", InstrumentType.Stock, "USD"));
            db.InvestmentTransactions.Add(new(Guid.CreateVersion7(), seed.PortfolioId, pendingInstrument, null,
                TradeSide.Buy, Time, 5m, 10m, 0m, executedAtOriginal: "2025-01-01T12:00:00Z"));
            await db.SaveChangesAsync();
        }
        using var response = await owner.Client.GetAsync(Holdings(seed));
        var result = await ReadAsync<HoldingsDetails>(response);
        Assert.False(result.IsComplete);
        Assert.Null(result.RealizedTotals);
        var complete = Assert.Single(result.Positions, p => p.Status == "Complete");
        Assert.Equal("18", complete.RealizedTotals!.ProfitUsd);
        var pending = Assert.Single(result.Positions, p => p.Status == "PendingRates");
        Assert.Equal("5", pending.Quantity);
        Assert.Null(pending.OpenLots);
        Assert.Null(pending.RealizedTotals);
    }

    [Fact]
    public async Task RejectedSplitCorrectionLeavesTheOriginalActiveAndNoReplacement()
    {
        using Actor admin = await ActorAsync(AccountRole.SuperAdmin);
        var seed = await SeedAsync(admin, saleQuantity: 2m);
        using var created = await admin.PostAsync(Splits(seed), Input());
        var original = await ReadAsync<SplitDetails>(created, HttpStatusCode.Created);
        using var rejected = await admin.PostAsync(Splits(seed) + $"/{original.Id}/corrections",
            new CorrectSplitInput(Input() with { Numerator = "1", Denominator = "3" }, "Invalid reverse split"));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var history = await admin.Client.GetAsync(Splits(seed) + "?includeSuperseded=true");
        var retained = Assert.Single((await ReadAsync<TradingPage<SplitDetails>>(history)).Items);
        Assert.Equal(original, retained);
        using var holdings = await admin.Client.GetAsync(Holdings(seed));
        Assert.Equal("4", Assert.Single((await ReadAsync<HoldingsDetails>(holdings)).Positions).Quantity);
    }

    [Fact]
    public async Task EmptyPortfolioReturnsCompleteZeroResults()
    {
        using Actor owner = await ActorAsync();
        Guid id = Guid.CreateVersion7();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Portfolios.Add(new(id, owner.Id, "Empty", "USD", Time));
            await db.SaveChangesAsync();
        }
        using var response = await owner.Client.GetAsync($"/api/portfolios/{id}/holdings");
        var result = await ReadAsync<HoldingsDetails>(response);
        Assert.True(result.IsComplete);
        Assert.False(result.IsTaxReady);
        Assert.Empty(result.Positions);
        Assert.Equal("0", result.RealizedTotals!.ProfitUsd);
        Assert.Equal("0", result.RealizedTotals.ProfitUah);
    }

    [Fact]
    public async Task FirstTradeWaitsForTheGlobalSplitWriteBoundary()
    {
        using Actor owner = await ActorAsync();
        Guid portfolio = Guid.CreateVersion7(), instrument = Guid.CreateVersion7();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(new Portfolio(portfolio, owner.Id, "First trade", "USD", Time),
            new Instrument(instrument, instrument.ToString("N")[..20], "TEST", "First trade", InstrumentType.Stock, "USD"));
        await db.SaveChangesAsync();

        await using var connection = new Npgsql.NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // Same transaction advisory key as LedgerWriteLock; emulate an in-progress split writer.
        await using var gate = new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        gate.Parameters.AddWithValue("key", 0x5A494150504C4544L);
        await gate.ExecuteNonQueryAsync();
        var request = owner.PostAsync($"/api/portfolios/{portfolio}/trades",
            new TradeInput(instrument, TradeSide.Buy, "2025-01-01T12:00:00Z", "3", "100", "1"));
        bool waiting = false;
        try
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!request.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                waiting = await db.Database.SqlQueryRaw<bool>("""
                    SELECT EXISTS (SELECT 1 FROM pg_locks
                        WHERE locktype = 'advisory' AND mode = 'ShareLock' AND NOT granted
                        AND database = (SELECT oid FROM pg_database WHERE datname = current_database())) AS "Value"
                    """).SingleAsync();
                if (waiting) { break; }
                await Task.Delay(25);
            }
        }
        finally { await transaction.CommitAsync(); }
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        response.EnsureSuccessStatusCode();
        Assert.True(waiting, "The first trade must wait on the shared ledger gate before reading splits.");
    }

    private static SplitInput Input() => new("2025-01-02T14:00:00+02:00", "2", "1", "Broker corporate-action notice");
    private static string Holdings(Seed seed) => $"/api/portfolios/{seed.PortfolioId}/holdings";
    private static string Splits(Seed seed) => $"/api/instruments/{seed.InstrumentId}/splits";
    private sealed record Seed(Guid PortfolioId, Guid InstrumentId, Guid BuyId, Guid? SaleId);

    private async Task<Seed> SeedAsync(Actor actor, decimal buyQuantity = 3m, decimal saleQuantity = 0m, bool verified = true, bool legacy = false)
    {
        Guid portfolioId = Guid.CreateVersion7(), instrumentId = Guid.CreateVersion7();
        var portfolio = new Portfolio(portfolioId, actor.Id, portfolioId.ToString(), "USD", Time);
        var instrument = new Instrument(instrumentId, instrumentId.ToString("N")[..20], "TEST", "Test stock", InstrumentType.Stock, "USD");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(portfolio, instrument);
        var existingRates = await db.ExchangeRates.Where(r => r.Source == NbuRateSource.Key).ToListAsync();
        InvestmentTransaction AddTrade(TradeSide side, DateTimeOffset time, decimal quantity, decimal price, decimal fee, decimal rateValue)
        {
            var existing = verified ? existingRates.SingleOrDefault(r => r.EffectiveDate == DateOnly.FromDateTime(time.DateTime)) : null;
            var rate = existing ?? new ExchangeRate(Guid.CreateVersion7(), "USD", DateOnly.FromDateTime(time.DateTime), rateValue,
                verified ? NbuRateSource.Key : instrumentId.ToString(), Time.AddMonths(1),
                verified ? DateOnly.FromDateTime(time.DateTime) : null, verified ? "https://bank.gov.ua/test" : null,
                verified ? new string('A', 64) : null, verified ? "[]" : null);
            var trade = new InvestmentTransaction(Guid.CreateVersion7(), portfolioId, instrumentId, legacy ? rate.Id : null, side,
                time, quantity, price, fee, executedAtOriginal: time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            if (verified)
            {
                trade.ResolveExchangeRate(rate, rate.EffectiveDate, TradeRatePolicy.Version, actor.Id, Time.AddMonths(1));
            }
            if ((verified || legacy) && existing is null) { db.ExchangeRates.Add(rate); existingRates.Add(rate); }
            db.InvestmentTransactions.Add(trade);
            return trade;
        }
        var buy = AddTrade(TradeSide.Buy, Time, buyQuantity, 100m, buyQuantity, 40m);
        var sale = saleQuantity > 0 ? AddTrade(TradeSide.Sell, Time.AddDays(2), saleQuantity, 120m, 1m, 41m) : null;
        await db.SaveChangesAsync();
        return new(portfolioId, instrumentId, buy.Id, sale?.Id);
    }

    private async Task<Actor> ActorAsync(AccountRole role = AccountRole.User)
    {
        const string password = "Holdings!Testing123";
        string email = $"holdings-{Guid.CreateVersion7():N}@example.test";
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
            var result = await service.ProvisionAsync(new(email, "Holder", password, role, SupportedLanguage.English));
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(document.RootElement.GetProperty("token").GetString());
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(JsonOptions));
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