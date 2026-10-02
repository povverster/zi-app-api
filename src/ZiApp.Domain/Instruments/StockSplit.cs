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
        decimal denominator,
        Guid? fifoOrderId = null,
        string? effectiveAtOriginal = null,
        Guid? recordedByAccountId = null,
        DateTimeOffset? recordedAtUtc = null,
        string? sourceReference = null,
        Guid? previousSplitId = null,
        string? correctionReason = null)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        InstrumentId = DomainGuard.RequiredId(instrumentId, nameof(instrumentId));
        EffectiveAtUtc = effectiveAtUtc;
        Numerator = DomainGuard.Positive(numerator, nameof(numerator));
        Denominator = DomainGuard.Positive(denominator, nameof(denominator));
        FifoOrderId = DomainGuard.RequiredId(fifoOrderId ?? id, nameof(fifoOrderId));
        EffectiveAtOriginal = effectiveAtOriginal;
        RecordedByAccountId = recordedByAccountId;
        RecordedAtUtc = recordedAtUtc;
        SourceReference = sourceReference;
        PreviousSplitId = previousSplitId;
        CorrectionReason = correctionReason;

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

    public Guid FifoOrderId { get; private set; }
    public string? EffectiveAtOriginal { get; private set; }
    public Guid? RecordedByAccountId { get; private set; }
    public DateTimeOffset? RecordedAtUtc { get; private set; }
    public string? SourceReference { get; private set; }
    public Guid? PreviousSplitId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public bool IsSuperseded { get; private set; }

    public void Supersede()
    {
        if (IsSuperseded) { throw new InvalidOperationException("This split was already corrected."); }
        IsSuperseded = true;
    }

    public Instrument Instrument { get; private set; } = null!;
}