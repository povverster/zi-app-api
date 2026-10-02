using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Application.Ledger;

public sealed class SplitService(ICurrentAccount account, ISplitRepository repository)
{
    public async Task<LedgerResult<TradingPage<SplitDetails>>> ListAsync(Guid instrumentId, int page, int pageSize,
        bool includeSuperseded, CancellationToken cancellationToken)
    {
        if (await account.GetActiveAccountIdAsync(cancellationToken) is null) { return new(null, LedgerError.AccountUnavailable); }
        if (!TradingValidation.IsPageValid(page, pageSize)) { return new(null, LedgerError.InvalidInput); }
        var result = await repository.ListAsync(instrumentId, page, pageSize, includeSuperseded, cancellationToken);
        return result is null ? new(null, LedgerError.NotFound) : new(result);
    }

    public async Task<LedgerResult<SplitDetails>> GetAsync(Guid instrumentId, Guid id, CancellationToken cancellationToken)
    {
        if (await account.GetActiveAccountIdAsync(cancellationToken) is null) { return new(null, LedgerError.AccountUnavailable); }
        var result = await repository.FindAsync(instrumentId, id, cancellationToken);
        return result is null ? new(null, LedgerError.NotFound) : new(result);
    }

    // Only the SuperAdmin-authorized controller may call shared catalog mutations.
    public async Task<LedgerResult<SplitDetails>> RecordAsync(Guid instrumentId, SplitInput input,
        Guid? originalId, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        Guid? actorId = await account.GetActiveAccountIdAsync(cancellationToken);
        if (actorId is null) { return new(null, LedgerError.AccountUnavailable); }
        if (!TradingValidation.TryInstant(input.EffectiveAt, out DateTimeOffset effectiveAt)
            || !TradingValidation.TryAmount(input.Numerator, false, out decimal numerator)
            || !TradingValidation.TryAmount(input.Denominator, false, out decimal denominator)
            || numerator == denominator || !TradingValidation.IsTextValid(input.SourceReference, 1000)
            || originalId is not null && !TradingValidation.IsTextValid(reason, 1000))
        { return new(null, LedgerError.InvalidInput); }
        return await repository.RecordAsync(actorId.Value, instrumentId, originalId, effectiveAt, input,
            numerator, denominator, reason, cancellationToken);
    }
}