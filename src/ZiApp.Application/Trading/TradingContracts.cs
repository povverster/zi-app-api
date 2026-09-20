using System.Globalization;

using ZiApp.Domain.Instruments;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Trading;

public enum TradingError
{
    AccountUnavailable, NotFound, InvalidInput, Duplicate, Archived, Oversold, AlreadyCorrected, UnsupportedCurrency,
}

public sealed record TradingResult<T>(T? Value, TradingError? Error = null) where T : class;

public sealed record TradingPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record InstrumentInput(string Symbol, string ExchangeCode, string Name, InstrumentType Type, string? Isin = null);

public sealed record InstrumentDetails(Guid Id, string Symbol, string ExchangeCode, string Name,
    InstrumentType Type, string CurrencyCode, string? Isin)
{
    public static InstrumentDetails From(Instrument value) =>
        new(value.Id, value.Symbol, value.ExchangeCode, value.Name, value.Type, value.CurrencyCode, value.Isin);
}

// Decimal strings are intentional: browser clients must not lose precision through JavaScript numbers.
public sealed record TradeInput(Guid InstrumentId, TradeSide Side, string ExecutedAt, string Quantity,
    string UnitPriceUsd, string FeeUsd, string? BrokerTransactionId = null);

public sealed record CorrectTradeInput(TradeInput Replacement, string Reason);

public sealed record TradeDetails(Guid Id, Guid FifoOrderId, Guid PortfolioId, Guid InstrumentId, TradeSide Side,
    DateTimeOffset ExecutedAtUtc, string? ExecutedAtOriginal, string Quantity, string UnitPriceUsd, string FeeUsd,
    string? BrokerTransactionId, Guid? ExchangeRateId, string RateStatus, bool IsTaxReady, bool IsSuperseded)
{
    public static TradeDetails From(InvestmentTransaction value) => new(
        value.Id, value.FifoOrderId, value.PortfolioId, value.InstrumentId, value.Side, value.ExecutedAtUtc,
        value.ExecutedAtOriginal,
        Format(value.Quantity), Format(value.UnitPriceUsd), Format(value.FeeUsd), value.BrokerTransactionId,
        value.ExchangeRateId, value.ExchangeRateId is null ? "Pending" : "LinkedUnverified", false, value.IsSuperseded);

    private static string Format(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}

public sealed record CorrectionDetails(Guid Id, Guid OriginalTradeId, Guid ReplacementTradeId,
    Guid ActorAccountId, string Reason, DateTimeOffset CreatedAtUtc)
{
    public static CorrectionDetails From(TradeCorrection value) => new(
        value.Id, value.OriginalTradeId, value.ReplacementTradeId, value.ActorAccountId, value.Reason, value.CreatedAtUtc);
}