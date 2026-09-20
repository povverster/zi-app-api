using ZiApp.Application.Security;
using ZiApp.Domain.Instruments;

namespace ZiApp.Application.Trading;

public sealed class InstrumentService(ICurrentAccount currentAccount, ITradingRepository repository)
{
    public async Task<TradingResult<TradingPage<InstrumentDetails>>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (await currentAccount.GetActiveAccountIdAsync(cancellationToken) is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        if (!TradingValidation.IsPageValid(page, pageSize)
            || search is not null && (search.Length > 100 || search.Contains('\0', StringComparison.Ordinal)))
        {
            return new(null, TradingError.InvalidInput);
        }

        return new(await repository.ListInstrumentsAsync(search?.Trim(), page, pageSize, cancellationToken));
    }

    public async Task<TradingResult<InstrumentDetails>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await currentAccount.GetActiveAccountIdAsync(cancellationToken) is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        Instrument? instrument = await repository.FindInstrumentAsync(id, cancellationToken);
        return instrument is null ? new(null, TradingError.NotFound) : new(InstrumentDetails.From(instrument));
    }

    // Called only through the SuperAdmin-authorized catalog endpoint.
    public async Task<TradingResult<InstrumentDetails>> CreateAsync(InstrumentInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await currentAccount.GetActiveAccountIdAsync(cancellationToken) is null)
        {
            return new(null, TradingError.AccountUnavailable);
        }

        if (!TradingValidation.IsTextValid(input.Symbol, 32)
            || !TradingValidation.IsTextValid(input.ExchangeCode, 32)
            || !TradingValidation.IsTextValid(input.Name, 300) || !Enum.IsDefined(input.Type)
            || input.Isin is not null && !string.IsNullOrWhiteSpace(input.Isin)
                && (input.Isin.Length > 12 || input.Isin.Trim().Length != 12 || !input.Isin.All(char.IsAsciiLetterOrDigit)))
        {
            return new(null, TradingError.InvalidInput);
        }

        var instrument = new Instrument(Guid.CreateVersion7(), input.Symbol, input.ExchangeCode,
            input.Name, input.Type, "USD", input.Isin);
        return await repository.AddInstrumentAsync(instrument, cancellationToken)
            ? new(InstrumentDetails.From(instrument)) : new(null, TradingError.Duplicate);
    }
}