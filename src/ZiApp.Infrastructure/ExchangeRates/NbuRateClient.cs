using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ZiApp.Application.ExchangeRates;
using ZiApp.Domain.ExchangeRates;

namespace ZiApp.Infrastructure.ExchangeRates;

public sealed partial class NbuRateClient(HttpClient httpClient, TimeProvider timeProvider) : INbuRateClient
{
    private const int MaxBodyBytes = 32768;
    private const int Attempts = 3;

    public static Uri SourceUri(DateOnly date) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://bank.gov.ua/NBU_Exchange/exchange_site?start={date:yyyyMMdd}&end={date:yyyyMMdd}&valcode=usd&sort=exchangedate&order=asc&json"));

    public async Task<RateResult<ExchangeRate>> FetchAsync(DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        Uri uri = SourceUri(effectiveDate);
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            TimeSpan delay = TimeSpan.FromMilliseconds(250 * (attempt + 1));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength > MaxBodyBytes)
                    {
                        return new(null, RateError.InvalidResponse);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    var bytes = new byte[MaxBodyBytes + 1];
                    int count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, timeout.Token);
                    return count > MaxBodyBytes ? new(null, RateError.InvalidResponse)
                        : Parse(effectiveDate, uri, bytes.AsMemory(0, count), timeProvider.GetUtcNow());
                }

                bool transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (!transient)
                {
                    return new(null, RateError.UpstreamUnavailable);
                }

                TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - timeProvider.GetUtcNow());
                if (retryAfter > TimeSpan.FromSeconds(2))
                {
                    // Do not retry earlier than NBU requests; let the caller retry later.
                    return new(null, RateError.UpstreamUnavailable);
                }

                if (retryAfter > delay) { delay = retryAfter.Value; }
            }
            catch (HttpRequestException)
            {
                // Bounded retries for transport failures, never for malformed successful payloads.
            }
            catch (IOException)
            {
                // A connection can also fail while streaming the response body.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-attempt timeout expired. Caller cancellation is always propagated.
            }

            if (attempt + 1 < Attempts)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }

        return new(null, RateError.UpstreamUnavailable);
    }

    private static RateResult<ExchangeRate> Parse(DateOnly date, Uri uri, ReadOnlyMemory<byte> bytes, DateTimeOffset retrieved)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) { return new(null, RateError.InvalidResponse); }
            if (root.GetArrayLength() == 0) { return new(null, RateError.RateMissing); }
            if (root.GetArrayLength() != 1) { return new(null, RateError.InvalidResponse); }
            JsonElement value = root[0];
            if (value.ValueKind != JsonValueKind.Object
                || value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || value.GetProperty("cc").GetString() != "USD" || value.GetProperty("r030").GetInt32() != 840
                || !value.GetProperty("units").TryGetInt32(out int units) || units != 1
                || !DateOnly.TryParseExact(value.GetProperty("exchangedate").GetString(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly effective) || effective != date
                || !DateOnly.TryParseExact(value.GetProperty("calcdate").GetString(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly calculation) || calculation > effective)
            {
                return new(null, RateError.InvalidResponse);
            }

            if (!TryRate(value.GetProperty("rate_per_unit"), out decimal rate)
                || !TryRate(value.GetProperty("rate"), out decimal quotedRate) || rate != quotedRate)
            {
                return new(null, RateError.InvalidResponse);
            }

            string raw = new UTF8Encoding(false, true).GetString(bytes.Span);
            return new(new ExchangeRate(Guid.CreateVersion7(), "USD", date, rate, NbuRateSource.Key, retrieved,
                calculation, uri.AbsoluteUri, Convert.ToHexString(SHA256.HashData(bytes.Span)), raw));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException or DecoderFallbackException)
        {
            return new(null, RateError.InvalidResponse);
        }
    }

    private static bool TryRate(JsonElement value, out decimal rate)
    {
        rate = 0m;
        return value.ValueKind == JsonValueKind.Number && RatePattern().IsMatch(value.GetRawText())
            && decimal.TryParse(value.GetRawText(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate)
            && rate > 0m;
    }

    [GeneratedRegex(@"\A[0-9]{1,10}(\.[0-9]{1,10})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex RatePattern();
}