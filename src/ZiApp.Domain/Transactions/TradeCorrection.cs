using ZiApp.Domain.Common;

namespace ZiApp.Domain.Transactions;

public sealed class TradeCorrection
{
    private TradeCorrection()
    {
    }

    public TradeCorrection(Guid id, Guid originalTradeId, Guid replacementTradeId,
        Guid actorAccountId, string reason, DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        OriginalTradeId = DomainGuard.RequiredId(originalTradeId, nameof(originalTradeId));
        ReplacementTradeId = DomainGuard.RequiredId(replacementTradeId, nameof(replacementTradeId));
        ActorAccountId = DomainGuard.RequiredId(actorAccountId, nameof(actorAccountId));
        Reason = DomainGuard.RequiredText(reason, 1000, nameof(reason));
        CreatedAtUtc = createdAtUtc;
        if (OriginalTradeId == ReplacementTradeId)
        {
            throw new ArgumentException("A correction requires a new replacement trade.", nameof(replacementTradeId));
        }
    }

    public Guid Id { get; private set; }
    public Guid OriginalTradeId { get; private set; }
    public Guid ReplacementTradeId { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}