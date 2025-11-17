namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class LocalOrderHistoryService : IOrderHistoryService
{
    private readonly IHistoryStore _store;

    public LocalOrderHistoryService(IHistoryStore store)
    {
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
    }

    public Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default)
    {
        return _store.QueryOrdersAsync(query, ct);
    }
}
