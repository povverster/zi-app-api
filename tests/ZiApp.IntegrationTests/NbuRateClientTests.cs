using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using ZiApp.Application.ExchangeRates;
using ZiApp.Infrastructure.ExchangeRates;

namespace ZiApp.IntegrationTests;

public sealed class NbuRateClientTests
{
    private static readonly DateOnly Day = new(2025, 1, 5);

    [Fact]
    public async Task ExactWeekendRateIncludesVerifiableProvenance()
    {
        string payload = Payload(Day);
        using var handler = new NbuTestHandler(_ => Task.FromResult(Response(payload)));
        using var http = new HttpClient(handler);
        var result = await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None);
        var rate = Assert.IsType<ZiApp.Domain.ExchangeRates.ExchangeRate>(result.Value);
        Assert.Equal(42.0385m, rate.RateToUah);
        Assert.Equal(Day, rate.EffectiveDate);
        Assert.Equal(Day.AddDays(-3), rate.CalculationDate);
        Assert.Equal(NbuRateSource.Key, rate.Source);
        Assert.Equal(NbuRateClient.SourceUri(Day).AbsoluteUri, rate.SourceUrl);
        Assert.Equal(payload, rate.RawResponseJson);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), rate.ResponseSha256);
        Assert.Equal(7, rate.Id.Version);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]", RateError.RateMissing)]
    [InlineData("{}", RateError.InvalidResponse)]
    [InlineData("null", RateError.InvalidResponse)]
    [InlineData("<html>offline</html>", RateError.InvalidResponse)]
    [InlineData("[{}]", RateError.InvalidResponse)]
    [InlineData("[{},{}]", RateError.InvalidResponse)]
    public async Task EmptyAndMalformedResultsAreNotCachedOrRetried(string payload, RateError expected)
    {
        using var handler = new NbuTestHandler(_ => Task.FromResult(Response(payload)));
        using var http = new HttpClient(handler);
        var result = await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Value);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("USD", "EUR")]
    [InlineData("840", "978")]
    [InlineData("05.01.2025", "04.01.2025")]
    [InlineData("02.01.2025", "06.01.2025")]
    [InlineData("42.0385", "0")]
    [InlineData("42.0385", "-1")]
    [InlineData("42.0385", "42.12345678901")]
    [InlineData("42.0385", "42.123456789000000000000000000001")]
    [InlineData("42.0385", "10000000000")]
    [InlineData("\"units\":1", "\"units\":100")]
    [InlineData("\"cc\":\"USD\"", "\"cc\":\"EUR\",\"cc\":\"USD\"")]
    [InlineData("\"rate_per_unit\":42.0385", "\"rate_per_unit\":42.0386")]
    public async Task WrongCurrencyDateUnitsPrecisionAndDuplicateFieldsFailClosed(string oldText, string newText)
    {
        using var handler = new NbuTestHandler(_ => Task.FromResult(Response(Payload(Day).Replace(oldText, newText, StringComparison.Ordinal))));
        using var http = new HttpClient(handler);
        var result = await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None);
        Assert.Equal(RateError.InvalidResponse, result.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(408, 3)]
    [InlineData(429, 3)]
    [InlineData(500, 3)]
    [InlineData(503, 3)]
    [InlineData(403, 1)]
    [InlineData(302, 1)]
    public async Task RetriesAreBoundedAndOnlyForTransientStatusCodes(int status, int expectedCalls)
    {
        using var handler = new NbuTestHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var result = await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None);
        Assert.Equal(RateError.UpstreamUnavailable, result.Error);
        Assert.Equal(expectedCalls, handler.Calls);
    }

    [Fact]
    public async Task TransportFailureCanRecoverAndCallerCancellationIsNotRetried()
    {
        int attempt = 0;
        using var handler = new NbuTestHandler(_ => ++attempt == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated outage")) : Task.FromResult(Response(Payload(Day))));
        using var http = new HttpClient(handler);
        Assert.NotNull((await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None)).Value);
        Assert.Equal(2, handler.Calls);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, cancellation.Token));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task OversizedPayloadAndLongRetryAfterAreBounded()
    {
        using var handler = new NbuTestHandler(_ => Task.FromResult(Response(new string('x', 32769))));
        using var http = new HttpClient(handler);
        Assert.Equal(RateError.InvalidResponse, (await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None)).Error);
        handler.Reply = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        };
        Assert.Equal(RateError.UpstreamUnavailable, (await new NbuRateClient(http, TimeProvider.System).FetchAsync(Day, CancellationToken.None)).Error);
        Assert.Equal(2, handler.Calls);
    }

    internal static string Payload(DateOnly date) => string.Create(CultureInfo.InvariantCulture,
        $$"""[{"exchangedate":"{{date:dd.MM.yyyy}}","r030":840,"cc":"USD","rate":42.0385,"units":1,"rate_per_unit":42.0385,"calcdate":"{{date.AddDays(-3):dd.MM.yyyy}}"}]""");

    internal static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json"),
    };
}

internal sealed class NbuTestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply { get; set; } = reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Reply(request);
    }
}