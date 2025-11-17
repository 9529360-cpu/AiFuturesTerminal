namespace AiFuturesTerminal.Core.Exchanges.History;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Exchanges.Binance;
using Microsoft.Extensions.Logging;

public sealed class BinanceOrderHistoryService : AiFuturesTerminal.Core.History.IOrderHistoryService
{
    private readonly BinanceAdapter _client;
    private readonly IHistoryStore _store;
    private readonly ILogger<BinanceOrderHistoryService>? _logger;

    public BinanceOrderHistoryService(BinanceAdapter client, IHistoryStore store, ILogger<BinanceOrderHistoryService>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default)
    {
        var local = await _store.QueryOrdersAsync(query, ct).ConfigureAwait(false);
        var results = new List<OrderHistoryRecord>(local ?? Array.Empty<OrderHistoryRecord>());

        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromMinutes(5);
        bool shouldPatch = !string.IsNullOrWhiteSpace(query.Symbol) && (now - query.To) <= threshold;

        if (shouldPatch)
        {
            try
            {
                // fetch recent orders via API
                var from = query.From.UtcDateTime;
                var to = now.UtcDateTime;
                var doc = await _client.GetAllOrdersAsync(query.Symbol!, from, to, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var el in root.EnumerateArray())
                            {
                                try
                                {
                                    var o = Map(el);
                                    if (o != null) results.Add(o);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { _logger?.LogWarning(ex, "Failed to patch recent orders from Binance"); } catch { }
            }
        }

        // dedupe by (OrderId, Symbol)
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var final = new List<OrderHistoryRecord>();
        foreach (var o in results.OrderBy(x => x.CreateTime))
        {
            var key = $"{o.ExchangeOrderId}:{o.Symbol}";
            if (set.Add(key)) final.Add(o);
        }

        var filtered = final.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.StrategyId)) filtered = filtered.Where(o => string.Equals(o.StrategyId, query.StrategyId, StringComparison.OrdinalIgnoreCase));

        var skip = (query.Page - 1) * query.PageSize;
        return filtered.Skip(skip).Take(query.PageSize).ToArray();
    }

    private static OrderHistoryRecord? Map(System.Text.Json.JsonElement el)
    {
        try
        {
            var oid = el.GetProperty("orderId").GetInt64();
            var symbol = el.GetProperty("symbol").GetString() ?? string.Empty;
            var price = el.TryGetProperty("price", out var p) && p.ValueKind != System.Text.Json.JsonValueKind.Null ? decimal.Parse(p.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var origQty = el.TryGetProperty("origQty", out var o) && o.ValueKind != System.Text.Json.JsonValueKind.Null ? decimal.Parse(o.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var executedQty = el.TryGetProperty("executedQty", out var e) && e.ValueKind != System.Text.Json.JsonValueKind.Null ? decimal.Parse(e.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var avgPrice = el.TryGetProperty("avgPrice", out var a) && a.ValueKind != System.Text.Json.JsonValueKind.Null ? decimal.Parse(a.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var status = el.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var type = el.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var createTime = el.TryGetProperty("time", out var ct) && ct.ValueKind != System.Text.Json.JsonValueKind.Null ? DateTimeOffset.FromUnixTimeMilliseconds(ct.GetInt64()).UtcDateTime : DateTime.UtcNow;
            var updateTime = el.TryGetProperty("updateTime", out var ut) && ut.ValueKind != System.Text.Json.JsonValueKind.Null ? DateTimeOffset.FromUnixTimeMilliseconds(ut.GetInt64()).UtcDateTime : createTime;

            string? clientOrderId = null;
            if (el.TryGetProperty("clientOrderId", out var co) && co.ValueKind == System.Text.Json.JsonValueKind.String) clientOrderId = co.GetString();

            string? strategyId = null;
            if (!string.IsNullOrWhiteSpace(clientOrderId))
            {
                if (AiFuturesTerminal.Core.Strategy.StrategyOrderTag.TryParse(clientOrderId, out var sid, out var run, out var tag)) strategyId = sid;
            }

            return new OrderHistoryRecord
            {
                ExchangeOrderId = oid,
                Symbol = symbol,
                CreateTime = createTime,
                UpdateTime = updateTime,
                Status = status,
                Type = type,
                OrigQty = origQty,
                ExecutedQty = executedQty,
                Price = price,
                AvgPrice = avgPrice,
                StrategyId = strategyId,
                ClientOrderId = clientOrderId
            };
        }
        catch
        {
            return null;
        }
    }
}