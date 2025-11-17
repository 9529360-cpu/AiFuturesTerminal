namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class LocalTradeHistoryService : ITradeHistoryService
{
    private readonly IHistoryStore _store;

    public LocalTradeHistoryService(IHistoryStore store)
    {
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
    }

    public Task<IReadOnlyList<TradeHistoryRecord>> QueryTradesAsync(HistoryQuery query, CancellationToken ct = default)
    {
        // Delegate directly to IHistoryStore implementation
        return _store.QueryTradesAsync(query, ct);
    }
}
