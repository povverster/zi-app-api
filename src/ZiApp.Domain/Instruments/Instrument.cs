using ZiApp.Domain.Common;

namespace ZiApp.Domain.Instruments;

public enum InstrumentType
{
    Stock = 1,
    Etf = 2,
}

public sealed class Instrument
{
    private Instrument()
    {
    }

    public Instrument(
        Guid id,
        string symbol,
        string exchangeCode,
        string name,
        InstrumentType type,
        string currencyCode,
        string? isin = null)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        Symbol = DomainGuard.RequiredText(symbol, 32, nameof(symbol)).ToUpperInvariant();
        ExchangeCode = DomainGuard.RequiredText(exchangeCode, 32, nameof(exchangeCode)).ToUpperInvariant();
        Name = DomainGuard.RequiredText(name, 300, nameof(name));
        Type = DomainGuard.DefinedEnum(type, nameof(type));
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        Isin = NormalizeIsin(isin);
    }

    public Guid Id { get; private set; }

    public string Symbol { get; private set; } = null!;

    public string ExchangeCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public InstrumentType Type { get; private set; }

    public string CurrencyCode { get; private set; } = null!;

    public string? Isin { get; private set; }

    private static string NormalizeCurrencyCode(string value)
    {
        string currencyCode = DomainGuard.RequiredText(value, 3, nameof(value)).ToUpperInvariant();
        if (currencyCode.Length != 3)
        {
            throw new ArgumentException("Currency code must contain exactly three characters.", nameof(value));
        }

        return currencyCode;
    }

    private static string? NormalizeIsin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string isin = DomainGuard.RequiredText(value, 12, nameof(value)).ToUpperInvariant();
        if (isin.Length != 12)
        {
            throw new ArgumentException("ISIN must contain exactly twelve characters.", nameof(value));
        }

        return isin;
    }
}