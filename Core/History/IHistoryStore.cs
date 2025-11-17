namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IHistoryStore
{
    Task UpsertTradesAsync(IEnumerable<TradeHistoryRecord> trades, CancellationToken ct = default);
    Task UpsertOrdersAsync(IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default);
    Task UpsertTradesAndOrdersAsync(IEnumerable<TradeHistoryRecord> trades, IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default);

    Task<IReadOnlyList<TradeHistoryRecord>> QueryTradesAsync(HistoryQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default);

    // meta helpers for sync state
    Task<string?> GetMetaAsync(string key, CancellationToken ct = default);
    Task SetMetaAsync(string key, string? value, CancellationToken ct = default);

    // list meta key/value pairs matching a prefix (useful for listing backtest runs)
    Task<IReadOnlyList<(string Key, string Value)>> ListMetaAsync(string keyPrefix, int limit = 100, CancellationToken ct = default);
}
