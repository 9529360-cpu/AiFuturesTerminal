namespace AiFuturesTerminal.Core.Exchanges.History;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Exchanges.Binance;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using AiFuturesTerminal.Core.Environment;
using AiFuturesTerminal.Core.Execution;

public sealed class BinanceTradeHistoryService : AiFuturesTerminal.Core.History.ITradeHistoryService
{
    private readonly BinanceAdapter _client;
    private readonly TimeSpan _maxWindow = TimeSpan.FromDays(7);
    private readonly IHistoryStore _store;
    private readonly ILogger<BinanceTradeHistoryService>? _logger;
    private readonly AppEnvironmentOptions _envOptions;

    public BinanceTradeHistoryService(BinanceAdapter client, IHistoryStore store, AppEnvironmentOptions envOptions, ILogger<BinanceTradeHistoryService>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
        _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
    }

    public async Task<IReadOnlyList<TradeHistoryRecord>> QueryTradesAsync(HistoryQuery query, CancellationToken ct = default)
    {
        // If running in Testnet mode, bypass local history DB and use Binance userTrades exclusively
        if (_envOptions.ExecutionMode == ExecutionMode.Testnet)
        {
            var results = new List<TradeHistoryRecord>();
            try
            {
                if (string.IsNullOrWhiteSpace(query.Symbol))
                {
                    // Binance userTrades requires symbol; return empty for unspecified symbol
                    return Array.Empty<TradeHistoryRecord>();
                }

                var from = query.From.UtcDateTime;
                var to = query.To.UtcDateTime;

                using var doc = await _client.GetUserTradesAsync(query.Symbol!, from, to, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in root.EnumerateArray())
                        {
                            try
                            {
                                var tr = Map(el);
                                if (tr != null) results.Add(tr);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { _logger?.LogWarning(ex, "Failed to fetch trades from Binance in Testnet mode"); } catch { }
            }

            // sort and return
            return results.OrderBy(t => t.Time).ToArray();
        }

        // Fallback behavior: read local store first, and patch recent trades from Binance when near realtime
        var local = await _store.QueryTradesAsync(query, ct).ConfigureAwait(false);
        var resultsList = new List<TradeHistoryRecord>(local ?? Array.Empty<TradeHistoryRecord>());

        // If symbol is specified and the query To is near now, fetch recent trades from Binance to patch live data
        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromMinutes(5);
        bool shouldPatch = !string.IsNullOrWhiteSpace(query.Symbol) && (now - query.To) <= threshold;

        if (shouldPatch)
        {
            try
            {
                var from = query.From.UtcDateTime;
                var to = now.UtcDateTime;

                using var doc = await _client.GetUserTradesAsync(query.Symbol!, from, to, ct).ConfigureAwait(false);
                if (doc != null)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in root.EnumerateArray())
                        {
                            try
                            {
                                var tr = Map(el);
                                if (tr != null)
                                    resultsList.Add(tr);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { _logger?.LogWarning(ex, "Failed to patch recent trades from Binance"); } catch { }
            }
        }

        // dedupe by (TradeId, Symbol)
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var final = new List<TradeHistoryRecord>();
        foreach (var t in resultsList.OrderBy(x => x.Time))
        {
            var key = $"{t.TradeId}:{t.Symbol}";
            if (set.Add(key)) final.Add(t);
        }

        // apply additional filters (strategyId, side) if provided in query
        var filtered = final.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.StrategyId)) filtered = filtered.Where(t => string.Equals(t.StrategyId, query.StrategyId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Side)) filtered = filtered.Where(t => string.Equals(t.Side, query.Side, StringComparison.OrdinalIgnoreCase));

        // paging
        var skip = (query.Page - 1) * query.PageSize;
        return filtered.Skip(skip).Take(query.PageSize).ToArray();
    }

    private static TradeHistoryRecord? Map(JsonElement el)
    {
        try
        {
            var tradeId = el.GetProperty("id").GetInt64();
            var orderId = el.GetProperty("orderId").GetInt64();
            var symbol = el.GetProperty("symbol").GetString() ?? string.Empty;
            var price = decimal.Parse(el.GetProperty("price").GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var qty = decimal.Parse(el.GetProperty("qty").GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var quoteQty = decimal.Parse(el.GetProperty("quoteQty").GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var commission = el.TryGetProperty("commission", out var c) && c.ValueKind != JsonValueKind.Null ? decimal.Parse(c.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var commissionAsset = el.TryGetProperty("commissionAsset", out var ca) && ca.ValueKind != JsonValueKind.Null ? ca.GetString() ?? string.Empty : string.Empty;
            var time = DateTimeOffset.FromUnixTimeMilliseconds(el.GetProperty("time").GetInt64());

            var isBuyer = el.TryGetProperty("isBuyer", out var ib) && ib.ValueKind == JsonValueKind.True;
            var side = isBuyer ? "BUY" : "SELL";
            var positionSide = el.TryGetProperty("positionSide", out var ps) && ps.ValueKind == JsonValueKind.String ? ps.GetString() ?? string.Empty : string.Empty;

            string? clientOrderId = null;
            if (el.TryGetProperty("clientOrderId", out var co) && co.ValueKind == JsonValueKind.String)
                clientOrderId = co.GetString();

            string? strategyId = null;
            if (!string.IsNullOrWhiteSpace(clientOrderId))
            {
                if (AiFuturesTerminal.Core.Strategy.StrategyOrderTag.TryParse(clientOrderId, out var sid, out var run, out var s))
                {
                    strategyId = sid;
                }
            }

            return new TradeHistoryRecord
            {
                TradeId = tradeId,
                OrderId = orderId,
                Symbol = symbol,
                Side = side,
                PositionSide = positionSide ?? string.Empty,
                Price = price,
                Qty = qty,
                QuoteQty = quoteQty,
                RealizedPnl = 0m,
                Commission = commission,
                CommissionAsset = commissionAsset ?? string.Empty,
                Time = time,
                StrategyId = strategyId
            };
        }
        catch
        {
            return null;
        }
    }
}