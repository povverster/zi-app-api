using ZiApp.Application.Security;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Trading;

public sealed class TradeService(ICurrentAccount currentAccount, ITradingRepository repository)
{
    public async Task<TradingResult<TradingPage<TradeDetails>>> ListAsync(Guid portfolioId, int page, int pageSize,
        bool includeSuperseded, CancellationToken cancellationToken)
    {
        Guid? ownerId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (ownerId is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        if (!TradingValidation.IsPageValid(page, pageSize))
        {
            return new(null, TradingError.InvalidInput);
        }

        var result = await repository.ListTradesAsync(ownerId.Value, portfolioId, page, pageSize,
            includeSuperseded, cancellationToken);
        return result is null ? new(null, TradingError.NotFound) : new(result);
    }

    public async Task<TradingResult<TradeDetails>> GetAsync(Guid portfolioId, Guid id, CancellationToken cancellationToken)
    {
        Guid? ownerId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (ownerId is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        TradeDetails? result = await repository.FindTradeAsync(ownerId.Value, portfolioId, id, cancellationToken);
        return result is null ? new(null, TradingError.NotFound) : new(result);
    }

    public async Task<TradingResult<TradingPage<CorrectionDetails>>> ListCorrectionsAsync(
        Guid portfolioId, int page, int pageSize, CancellationToken cancellationToken)
    {
        Guid? ownerId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (ownerId is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        if (!TradingValidation.IsPageValid(page, pageSize))
        {
            return new(null, TradingError.InvalidInput);
        }

        var result = await repository.ListCorrectionsAsync(ownerId.Value, portfolioId, page, pageSize, cancellationToken);
        return result is null ? new(null, TradingError.NotFound) : new(result);
    }

    public async Task<TradingResult<TradeDetails>> RecordAsync(Guid portfolioId, TradeInput input,
        Guid? originalId, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        Guid? ownerId = await currentAccount.GetActiveAccountIdAsync(cancellationToken);
        if (ownerId is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        if (input.InstrumentId == Guid.Empty || !Enum.IsDefined(input.Side)
            || !TradingValidation.TryInstant(input.ExecutedAt, out DateTimeOffset executedAt)
            || !TradingValidation.TryAmount(input.Quantity, false, out decimal quantity)
            || !TradingValidation.TryAmount(input.UnitPriceUsd, false, out decimal price)
            || !TradingValidation.TryAmount(input.FeeUsd, true, out decimal fee)
            || input.BrokerTransactionId is not null
                && (input.BrokerTransactionId.Length > 200 || input.BrokerTransactionId.Contains('\0', StringComparison.Ordinal))
            || originalId is not null && !TradingValidation.IsTextValid(reason, 1000))
        {
            return new(null, TradingError.InvalidInput);
        }

        return await repository.MutateAsync(ownerId.Value, portfolioId, input.InstrumentId, ledger =>
        {
            if (ledger.Portfolio.IsArchived)
            {
                return new(null, TradingError.Archived);
            }

            InvestmentTransaction? original = originalId is null ? null : ledger.Trades.SingleOrDefault(item => item.Id == originalId);
            if (originalId is not null && original is null || ledger.Instrument is null)
            {
                return new(null, TradingError.NotFound);
            }

            if (original?.IsSuperseded == true)
            {
                return new(null, TradingError.AlreadyCorrected);
            }

            if (ledger.Instrument.CurrencyCode != "USD" || ledger.Portfolio.BaseCurrencyCode != "USD")
            {
                return new(null, TradingError.UnsupportedCurrency);
            }

            var replacement = new InvestmentTransaction(Guid.CreateVersion7(), portfolioId, input.InstrumentId,
                null, input.Side, executedAt, quantity, price, fee, input.BrokerTransactionId, original?.FifoOrderId, input.ExecutedAt);
            var activeTrades = ledger.Trades.Where(item => !item.IsSuperseded && item.Id != originalId).ToList();
            if (replacement.BrokerTransactionId is not null
                && activeTrades.Any(item => item.BrokerTransactionId == replacement.BrokerTransactionId))
            {
                return new(null, TradingError.Duplicate);
            }

            activeTrades.Add(replacement);
            try
            {
                // Check all later sales too: backdating or correcting a buy can invalidate them.
                if (!TradeQuantityValidator.CanExecute(activeTrades, ledger.Splits))
                {
                    return new(null, TradingError.Oversold);
                }
            }
            catch (OverflowException)
            {
                return new(null, TradingError.InvalidInput);
            }

            TradeCorrection? correction = original is null ? null : new TradeCorrection(
                Guid.CreateVersion7(), original.Id, replacement.Id, ownerId.Value, reason!, DateTimeOffset.UtcNow);
            return new(new TradeMutation(replacement, original, correction));
        }, cancellationToken);
    }
}