using ZiApp.Domain.Common;

namespace ZiApp.Domain.ExchangeRates;

public sealed class ExchangeRate
{
    private ExchangeRate()
    {
    }

    public ExchangeRate(
        Guid id,
        string currencyCode,
        DateOnly effectiveDate,
        decimal rateToUah,
        string source,
        DateTimeOffset retrievedAtUtc,
        DateOnly? calculationDate = null,
        string? sourceUrl = null,
        string? responseSha256 = null,
        string? rawResponseJson = null)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        EffectiveDate = effectiveDate;
        RateToUah = DomainGuard.Positive(rateToUah, nameof(rateToUah));
        Source = DomainGuard.RequiredText(source, 100, nameof(source));
        RetrievedAtUtc = retrievedAtUtc;
        CalculationDate = calculationDate;
        SourceUrl = sourceUrl;
        ResponseSha256 = responseSha256;
        RawResponseJson = rawResponseJson;
    }

    public Guid Id { get; private set; }

    public string CurrencyCode { get; private set; } = null!;

    public DateOnly EffectiveDate { get; private set; }

    public decimal RateToUah { get; private set; }

    public string Source { get; private set; } = null!;

    public DateTimeOffset RetrievedAtUtc { get; private set; }

    public DateOnly? CalculationDate { get; private set; }

    public string? SourceUrl { get; private set; }

    public string? ResponseSha256 { get; private set; }

    public string? RawResponseJson { get; private set; }

    private static string NormalizeCurrencyCode(string value)
    {
        string currencyCode = DomainGuard.RequiredText(value, 3, nameof(value)).ToUpperInvariant();
        if (currencyCode.Length != 3)
        {
            throw new ArgumentException("Currency code must contain exactly three characters.", nameof(value));
        }

        return currencyCode;
    }
}