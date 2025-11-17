namespace AiFuturesTerminal.Core.History;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AiFuturesTerminal.Core.Exchanges.History;

public sealed class HistorySyncService : IHistorySyncService, IDisposable
{
    private readonly IHistoryStore _store;
    private readonly ITradeHistoryService _tradeApi;
    private readonly IOrderHistoryService _orderApi;
    private readonly ILogger<HistorySyncService>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _backgroundTask;

    public HistorySyncService(IHistoryStore store, ITradeHistoryService tradeApi, IOrderHistoryService orderApi, ILogger<HistorySyncService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tradeApi = tradeApi ?? throw new ArgumentNullException(nameof(tradeApi));
        _orderApi = orderApi ?? throw new ArgumentNullException(nameof(orderApi));
        _logger = logger;
    }

    public async Task SyncRecentTradesAsync(CancellationToken ct = default)
    {
        // default: sync recent 7 days
        await SyncRecentTradesAsync(days: 7, ct).ConfigureAwait(false);
    }

    public async Task SyncRecentOrdersAsync(CancellationToken ct = default)
    {
        await SyncRecentOrdersAsync(days: 7, ct).ConfigureAwait(false);
    }

    public async Task SyncRecentTradesAsync(int days, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-days);
        var to = now;

        var query = new HistoryQuery { From = from, To = to, Page = 1, PageSize = 1000 };

        // Strategy: query per symbol is better, but as an MVP, ask trade API with no symbol will likely fail for Binance; use known symbols from watch config later
        var trades = await _tradeApi.QueryTradesAsync(query, ct).ConfigureAwait(false);
        if (trades != null && trades.Count > 0)
        {
            await _store.UpsertTradesAsync(trades, ct).ConfigureAwait(false);
            _logger?.LogInformation($"HistorySync: inserted {trades.Count} trades.");
        }
    }

    public async Task SyncRecentOrdersAsync(int days, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-days);
        var to = now;

        var query = new HistoryQuery { From = from, To = to, Page = 1, PageSize = 1000 };
        var orders = await _orderApi.QueryOrdersAsync(query, ct).ConfigureAwait(false);
        if (orders != null && orders.Count > 0)
        {
            await _store.UpsertOrdersAsync(orders, ct).ConfigureAwait(false);
            _logger?.LogInformation($"HistorySync: inserted {orders.Count} orders.");
        }
    }

    // start background incremental task syncing every interval (minutes)
    public void StartBackgroundSync(TimeSpan interval)
    {
        if (_backgroundTask != null) return;
        _backgroundTask = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await SyncRecentTradesAsync(1, _cts.Token).ConfigureAwait(false);
                    await SyncRecentOrdersAsync(1, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger?.LogWarning(ex, "HistorySync background error"); }

                try { await Task.Delay(interval, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }, _cts.Token);
    }

    public void StopBackgroundSync()
    {
        try { _cts.Cancel(); } catch { }
        try { _backgroundTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
    }

    public void Dispose()
    {
        StopBackgroundSync();
        try { _cts.Dispose(); } catch { }
    }
}
