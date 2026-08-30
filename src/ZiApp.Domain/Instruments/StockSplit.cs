using ZiApp.Domain.Common;

namespace ZiApp.Domain.Instruments;

public sealed class StockSplit
{
    private StockSplit()
    {
    }

    public StockSplit(
        Guid id,
        Guid instrumentId,
        DateTimeOffset effectiveAtUtc,
        decimal numerator,
        decimal denominator)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        InstrumentId = DomainGuard.RequiredId(instrumentId, nameof(instrumentId));
        EffectiveAtUtc = effectiveAtUtc;
        Numerator = DomainGuard.Positive(numerator, nameof(numerator));
        Denominator = DomainGuard.Positive(denominator, nameof(denominator));

        if (Numerator == Denominator)
        {
            throw new ArgumentException("A split must change the number of units.", nameof(numerator));
        }
    }

    public Guid Id { get; private set; }

    public Guid InstrumentId { get; private set; }

    public DateTimeOffset EffectiveAtUtc { get; private set; }

    public decimal Numerator { get; private set; }

    public decimal Denominator { get; private set; }

    public Instrument Instrument { get; private set; } = null!;
}