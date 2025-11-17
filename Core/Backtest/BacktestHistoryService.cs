namespace AiFuturesTerminal.Core.Backtest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.History;
using Microsoft.Extensions.Logging;

public sealed class BacktestHistoryService : IBacktestHistoryService
{
    private readonly IHistoryStore _store;
    private readonly ILogger<BacktestHistoryService>? _logger;

    public BacktestHistoryService(IHistoryStore store, ILogger<BacktestHistoryService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
    }

    // Persist trades and orders from a backtest run into history store
    public async Task PersistBacktestAsync(string runId, IEnumerable<TradeHistoryRecord> trades, IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default)
    {
        // tag trades/orders with RunId and StrategyId if available, and AccountEnv = "Backtest"
        var tlist = new List<TradeHistoryRecord>();
        foreach (var t in trades)
        {
            var copy = t with { StrategyId = t.StrategyId ?? string.Empty, RunId = runId };
            // ensure AccountEnv handled by store (we currently leave AccountEnv blank in record, Sqlite store inserts empty string)
            tlist.Add(copy);
        }

        var olist = new List<OrderHistoryRecord>();
        foreach (var o in orders)
        {
            var copy = o with { StrategyId = o.StrategyId ?? string.Empty, RunId = runId };
            olist.Add(copy);
        }

        // use combined upsert for atomicity
        await _store.UpsertTradesAndOrdersAsync(tlist, olist, ct).ConfigureAwait(false);

        // record run meta as JSON
        var info = System.Text.Json.JsonSerializer.Serialize(new { RunId = runId, StrategyId = trades.FirstOrDefault()?.StrategyId ?? string.Empty, CreatedAt = DateTimeOffset.UtcNow, Env = "Backtest" });
        await _store.SetMetaAsync($"backtest:run:{runId}", info, ct).ConfigureAwait(false);

        _logger?.LogInformation($"BacktestHistory: persisted run {runId} trades={tlist.Count} orders={olist.Count}");
    }

    public async Task<IReadOnlyList<BacktestRunInfo>> ListBacktestRunsAsync(int limit = 100, CancellationToken ct = default)
    {
        var runs = new List<BacktestRunInfo>();
        try
        {
            var metas = await _store.ListMetaAsync("backtest:run:", limit, ct).ConfigureAwait(false);
            foreach (var (key, value) in metas)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var doc = System.Text.Json.JsonDocument.Parse(value);
                    var root = doc.RootElement;
                    var runId = root.GetProperty("RunId").GetString() ?? string.Empty;
                    var sid = root.GetProperty("StrategyId").GetString() ?? string.Empty;
                    var created = root.GetProperty("CreatedAt").GetDateTimeOffset();
                    var notes = root.TryGetProperty("Notes", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
                    runs.Add(new BacktestRunInfo(runId, sid, created, notes));
                }
                catch (Exception ex)
                {
                    try { _logger?.LogWarning(ex, $"Failed to parse backtest run meta key={key}"); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            try { _logger?.LogWarning(ex, "Failed to list backtest run metas"); } catch { }
        }

        // sort by CreatedAt desc
        var ordered = runs.OrderByDescending(r => r.CreatedAt).Take(limit).ToList();
        return ordered;
    }
}

public interface IBacktestHistoryService
{
    Task PersistBacktestAsync(string runId, IEnumerable<TradeHistoryRecord> trades, IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default);

    Task<IReadOnlyList<BacktestRunInfo>> ListBacktestRunsAsync(int limit = 100, CancellationToken ct = default);
}

public sealed record BacktestRunInfo(string RunId, string StrategyId, DateTimeOffset CreatedAt, string? Notes);
