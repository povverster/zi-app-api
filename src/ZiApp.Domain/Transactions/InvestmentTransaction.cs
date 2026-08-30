using ZiApp.Domain.Common;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;

namespace ZiApp.Domain.Transactions;

public enum TradeSide
{
    Buy = 1,
    Sell = 2,
}

public sealed class InvestmentTransaction
{
    private InvestmentTransaction()
    {
    }

    public InvestmentTransaction(
        Guid id,
        Guid portfolioId,
        Guid instrumentId,
        Guid exchangeRateId,
        TradeSide side,
        DateTimeOffset executedAtUtc,
        decimal quantity,
        decimal unitPriceUsd,
        decimal feeUsd,
        string? brokerTransactionId = null)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        PortfolioId = DomainGuard.RequiredId(portfolioId, nameof(portfolioId));
        InstrumentId = DomainGuard.RequiredId(instrumentId, nameof(instrumentId));
        ExchangeRateId = DomainGuard.RequiredId(exchangeRateId, nameof(exchangeRateId));
        Side = DomainGuard.DefinedEnum(side, nameof(side));
        ExecutedAtUtc = executedAtUtc;
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        UnitPriceUsd = DomainGuard.Positive(unitPriceUsd, nameof(unitPriceUsd));
        FeeUsd = DomainGuard.NonNegative(feeUsd, nameof(feeUsd));
        BrokerTransactionId = NormalizeBrokerTransactionId(brokerTransactionId);
    }

    public Guid Id { get; private set; }

    public Guid PortfolioId { get; private set; }

    public Guid InstrumentId { get; private set; }

    public Guid ExchangeRateId { get; private set; }

    public TradeSide Side { get; private set; }

    public DateTimeOffset ExecutedAtUtc { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPriceUsd { get; private set; }

    public decimal FeeUsd { get; private set; }

    public string? BrokerTransactionId { get; private set; }

    public Portfolio Portfolio { get; private set; } = null!;

    public Instrument Instrument { get; private set; } = null!;

    public ExchangeRate ExchangeRate { get; private set; } = null!;

    private static string? NormalizeBrokerTransactionId(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : DomainGuard.RequiredText(value, 200, nameof(value));
    }
}