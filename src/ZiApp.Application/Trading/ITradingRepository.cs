using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.Transactions;

namespace ZiApp.Application.Trading;

public sealed record TradeLedger(Portfolio Portfolio, Instrument? Instrument,
    IReadOnlyList<InvestmentTransaction> Trades, IReadOnlyList<StockSplit> Splits);

public sealed record TradeMutation(InvestmentTransaction Replacement,
    InvestmentTransaction? Original = null, TradeCorrection? Correction = null);

public interface ITradingRepository
{
    Task<TradingPage<InstrumentDetails>> ListInstrumentsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<Instrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> AddInstrumentAsync(Instrument instrument, CancellationToken cancellationToken);
    Task<TradingPage<TradeDetails>?> ListTradesAsync(Guid ownerId, Guid portfolioId, int page, int pageSize,
        bool includeSuperseded, CancellationToken cancellationToken);
    Task<TradeDetails?> FindTradeAsync(Guid ownerId, Guid portfolioId, Guid tradeId, CancellationToken cancellationToken);
    Task<TradingPage<CorrectionDetails>?> ListCorrectionsAsync(Guid ownerId, Guid portfolioId,
        int page, int pageSize, CancellationToken cancellationToken);

    // The portfolio lock must cover loading, deciding, and saving the complete mutation atomically.
    Task<TradingResult<TradeDetails>> MutateAsync(Guid ownerId, Guid portfolioId, Guid instrumentId,
        Func<TradeLedger, TradingResult<TradeMutation>> decide, CancellationToken cancellationToken);
}