using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Globalization;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Exchanges.Binance;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Environment;
using Microsoft.Extensions.Logging;

namespace AiFuturesTerminal.Core.Exchanges
{
    public sealed class BinanceStateService : IBinanceState
    {
        private readonly BinanceAdapter _adapter;
        private readonly TimeSpan _reconcileInterval;
        private readonly object _sync = new();
        private readonly ILogger<BinanceStateService>? _logger;
        private AccountSnapshotDto _account = new AccountSnapshotDto { Equity = 0m, FreeBalance = 0m, Timestamp = DateTime.UtcNow };
        private List<PositionDto> _positions = new();
        private List<OrderDto> _openOrders = new();
        private List<TradeRecord> _recentTrades = new();
        private List<DailyPnlRow> _dailyPnl = new();

        private CancellationTokenSource? _cts;
        private Task? _bgTask;
        private bool _reconciledOnce = false;

        public event EventHandler? PositionsChanged;

        public BinanceStateService(BinanceAdapter adapter, AppEnvironmentOptions envOptions, ILogger<BinanceStateService>? logger = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _reconcileInterval = TimeSpan.FromSeconds(Math.Max(1, envOptions?.BinancePositionReconcileIntervalSeconds ?? 30));
            _logger = logger;
            // subscribe to adapter position updates
            _adapter.PositionChanged += OnAdapterPositionChanged;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                if (_bgTask != null && !_bgTask.IsCompleted) return;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _bgTask = Task.Run(() => RunBackgroundAsync(_cts.Token));
            }

            try
            {
                _logger?.LogInformation($"[BinanceState] 已启动，对账间隔 = {_reconcileInterval.TotalSeconds} 秒，数据源 = Binance");
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task RunBackgroundAsync(CancellationToken ct)
        {
            try
            {
                // initial full load (fetch positions) before starting user-data-stream
                await ReconcileOnceAsync(ct).ConfigureAwait(false);

                // start user data stream so we can receive near-real-time updates
                try { await _adapter.StartUserDataStreamAsync(ct).ConfigureAwait(false); } 
                catch (Exception ex) { _logger?.LogWarning($"[BinanceState] 启动用户数据流失败: {ex.Message}"); }

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_reconcileInterval, ct).ConfigureAwait(false);
                        await ReconcileOnceAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch { /* swallow per-run errors */ }
                }
            }
            finally
            {
                // nothing
            }
        }

        private async Task ReconcileOnceAsync(CancellationToken ct)
        {
            try
            {
                var account = await _adapter.GetAccountSnapshotAsync(ct).ConfigureAwait(false);

                // fetch positions from exchange via positionRisk endpoint
                var newPositions = await FetchOpenPositionsFromExchangeAsync(ct).ConfigureAwait(false);

                var openOrdersDoc = await _adapter.GetOpenOrdersAsync(null, ct).ConfigureAwait(false);
                var openOrders = new List<OrderDto>();
                if (openOrdersDoc != null)
                {
                    try
                    {
                        foreach (var el in openOrdersDoc.RootElement.EnumerateArray())
                        {
                            var ord = new OrderDto
                            {
                                Symbol = el.GetProperty("symbol").GetString() ?? string.Empty,
                                OrderId = el.GetProperty("orderId").GetRawText().Trim('"'),
                                Status = el.GetProperty("status").GetString() ?? string.Empty,
                                Price = decimal.TryParse(el.GetProperty("price").GetString() ?? "0", out var p) ? p : 0m,
                                Quantity = decimal.TryParse(el.GetProperty("origQty").GetString() ?? "0", out var q) ? q : 0m,
                                Timestamp = el.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeMilliseconds(t.GetInt64()).UtcDateTime : DateTime.UtcNow
                            };
                            openOrders.Add(ord);
                        }
                    }
                    catch { }
                }

                // income (recent 7 days)
                var now = DateTime.UtcNow;
                var incomes = await _adapter.GetIncomeAsync(now.AddDays(-7), now, ct).ConfigureAwait(false);
                var recentTrades = new List<TradeRecord>();
                var daily = new List<DailyPnlRow>();
                if (incomes != null)
                {
                    foreach (var inc in incomes)
                    {
                        var tr = new TradeRecord
                        {
                            OpenTime = inc.Time,
                            CloseTime = inc.Time,
                            Symbol = inc.Symbol ?? string.Empty,
                            Side = string.Equals(inc.PositionSide, "LONG", StringComparison.OrdinalIgnoreCase) ? TradeSide.Long : TradeSide.Short,
                            Quantity = inc.Quantity,
                            EntryPrice = 0m,
                            ExitPrice = 0m,
                            RealizedPnl = inc.Income,
                            Fee = inc.Commission,
                            StrategyName = "BinanceSync",
                            Mode = inc.Mode
                        };
                        recentTrades.Add(tr);

                        daily.Add(new DailyPnlRow { Date = inc.Time.Date, RealizedPnl = inc.Income, Commission = inc.Commission, Symbol = inc.Symbol ?? string.Empty });
                    }
                }

                bool positionsChanged = false;
                lock (_sync)
                {
                    _account = new AccountSnapshotDto { Equity = account.Equity, FreeBalance = account.FreeBalance, Timestamp = account.Timestamp };

                    // replace positions cache if different
                    if (!_reconciledOnce || HasPositionsChanged(_positions, newPositions))
                    {
                        _positions = newPositions.ToList();
                        positionsChanged = true;
                    }

                    _openOrders = openOrders;
                    _recentTrades = recentTrades;
                    _dailyPnl = daily;
                    _reconciledOnce = true;
                }

                if (positionsChanged)
                {
                    try
                    {
                        _logger?.LogInformation($"[BinanceState] 对账完成，当前持仓数量 = {newPositions.Count}");
                    }
                    catch { }

                    try { PositionsChanged?.Invoke(this, EventArgs.Empty); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[BinanceState] Reconcile 失败: {ex.Message}");
            }
        }

        private bool HasPositionsChanged(IReadOnlyList<PositionDto> oldList, IReadOnlyList<PositionDto> newList)
        {
            if (oldList == null && newList == null) return false;
            if (oldList == null) return newList.Count > 0;
            if (newList == null) return oldList.Count > 0;
            if (oldList.Count != newList.Count) return true;

            // compare by Symbol+Side+Quantity+EntryPrice
            var oldSet = new HashSet<string>(oldList.Select(p => $"{p.Symbol.ToUpperInvariant()}|{p.Side}|{p.Quantity}|{p.EntryPrice}"));
            foreach (var np in newList)
            {
                var key = $"{np.Symbol.ToUpperInvariant()}|{np.Side}|{np.Quantity}|{np.EntryPrice}";
                if (!oldSet.Contains(key)) return true;
            }

            return false;
        }

        private async Task<IReadOnlyList<PositionDto>> FetchOpenPositionsFromExchangeAsync(CancellationToken ct)
        {
            try
            {
                var doc = await _adapter.GetAllPositionRiskAsync(ct).ConfigureAwait(false);
                if (doc == null) return Array.Empty<PositionDto>();

                var list = new List<PositionDto>();
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return Array.Empty<PositionDto>();

                foreach (var el in root.EnumerateArray())
                {
                    try
                    {
                        var symbol = el.TryGetProperty("symbol", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                        var posAmtStr = el.TryGetProperty("positionAmt", out var pa) ? pa.GetString() ?? "0" : "0";
                        var entryPriceStr = el.TryGetProperty("entryPrice", out var ep) ? ep.GetString() ?? "0" : "0";
                        var markPriceStr = el.TryGetProperty("markPrice", out var mp) ? mp.GetString() ?? "0" : "0";

                        if (!decimal.TryParse(posAmtStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var posAmt)) posAmt = 0m;
                        if (!decimal.TryParse(entryPriceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var entryPrice)) entryPrice = 0m;
                        if (!decimal.TryParse(markPriceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var markPrice)) markPrice = 0m;

                        // try to read unRealizedProfit if present
                        decimal unrealized = 0m;
                        if (el.TryGetProperty("unRealizedProfit", out var up))
                        {
                            var upStr = up.ValueKind == JsonValueKind.String ? up.GetString() ?? "0" : up.GetRawText();
                            if (!decimal.TryParse(upStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var upv)) upv = 0m;
                            unrealized = upv;
                        }

                        if (posAmt == 0m) continue; // skip

                        var side = posAmt > 0 ? PositionSide.Long : PositionSide.Short;
                        var qty = Math.Abs(posAmt);

                        var notional = qty * markPrice;
                        var unrealizedPnl = unrealized;
                        // if unrealized not provided, compute fallback
                        if (unrealizedPnl == 0m)
                        {
                            unrealizedPnl = (markPrice - entryPrice) * posAmt; // posAmt signed
                        }

                        var dto = new PositionDto
                        {
                            Symbol = symbol,
                            Side = side,
                            Quantity = qty,
                            EntryPrice = entryPrice,
                            EntryTime = null,
                            MarkPrice = markPrice,
                            NotionalUsdt = decimal.Round(notional, 2),
                            UnrealizedPnlUsdt = decimal.Round(unrealizedPnl, 2)
                        };
                        list.Add(dto);
                    }
                    catch { }
                }

                return list;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[BinanceState] 拉取持仓失败: {ex.Message}");
                return Array.Empty<PositionDto>();
            }
        }

        public Task<AccountSnapshotDto> GetAccountAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                var copy = _account;
                return Task.FromResult(copy);
            }
        }

        public Task<IReadOnlyList<PositionDto>> GetOpenPositionsAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                return Task.FromResult((IReadOnlyList<PositionDto>)_positions.ToList());
            }
        }

        public IReadOnlyList<PositionDto> GetOpenPositionsSnapshot()
        {
            lock (_sync)
            {
                return _positions.ToArray();
            }
        }

        public async Task<PositionDto?> GetOpenPositionAsync(string symbol, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            try
            {
                var p = await _adapter.GetPositionRiskAsync(symbol, ct).ConfigureAwait(false);
                if (p == null || p.IsFlat()) return null;
                // map to PositionDto and fill new fields conservatively
                var qty = p.Quantity;
                var mark = p.EntryPrice; // fallback
                var notional = qty * mark;
                return new PositionDto
                {
                    Symbol = p.Symbol,
                    Side = p.Side,
                    Quantity = p.Quantity,
                    EntryPrice = p.EntryPrice,
                    EntryTime = p.EntryTime,
                    MarkPrice = mark,
                    NotionalUsdt = decimal.Round(notional, 2),
                    UnrealizedPnlUsdt = 0m
                };
            }
            catch { return null; }
        }

        private void OnAdapterPositionChanged(object? sender, BinanceAdapter.PositionUpdateEventArgs e)
        {
            if (e == null) return;

            PositionSide side;
            if (string.Equals(e.Side, "LONG", StringComparison.OrdinalIgnoreCase)) side = PositionSide.Long;
            else if (string.Equals(e.Side, "SHORT", StringComparison.OrdinalIgnoreCase)) side = PositionSide.Short;
            else side = e.PositionAmt > 0 ? PositionSide.Long : PositionSide.Short;

            var qty = Math.Abs(e.PositionAmt);
            var notional = qty * e.MarkPrice;
            var unrealized = (e.MarkPrice - e.EntryPrice) * e.PositionAmt;

            var dto = new PositionDto
            {
                Symbol = e.Symbol,
                Side = side,
                Quantity = qty,
                EntryPrice = e.EntryPrice,
                EntryTime = null,
                MarkPrice = e.MarkPrice,
                NotionalUsdt = decimal.Round(notional, 2),
                UnrealizedPnlUsdt = decimal.Round(unrealized, 2)
            };

            ApplyPositionUpdateFromUserStream(dto);
        }

        public void ApplyPositionUpdateFromUserStream(PositionDto updated)
        {
            if (updated == null) return;
            bool raised = false;
            lock (_sync)
            {
                var existing = _positions.FirstOrDefault(p => string.Equals(p.Symbol, updated.Symbol, StringComparison.OrdinalIgnoreCase) && p.Side == updated.Side);
                if (existing != null)
                {
                    if (updated.Quantity == 0m)
                    {
                        _positions.Remove(existing);
                        raised = true;
                    }
                    else
                    {
                        _positions.Remove(existing);
                        _positions.Add(new PositionDto
                        {
                            Symbol = updated.Symbol,
                            Side = updated.Side,
                            Quantity = updated.Quantity,
                            EntryPrice = updated.EntryPrice,
                            EntryTime = updated.EntryTime,
                            MarkPrice = updated.MarkPrice,
                            NotionalUsdt = updated.NotionalUsdt,
                            UnrealizedPnlUsdt = updated.UnrealizedPnlUsdt
                        });
                        raised = true;
                    }
                }
                else
                {
                    if (updated.Quantity > 0m)
                    {
                        _positions.Add(new PositionDto
                        {
                            Symbol = updated.Symbol,
                            Side = updated.Side,
                            Quantity = updated.Quantity,
                            EntryPrice = updated.EntryPrice,
                            EntryTime = updated.EntryTime,
                            MarkPrice = updated.MarkPrice,
                            NotionalUsdt = updated.NotionalUsdt,
                            UnrealizedPnlUsdt = updated.UnrealizedPnlUsdt
                        });
                        raised = true;
                    }
                }
            }

            if (raised)
            {
                try { _logger?.LogInformation($"[BinanceState] User stream applied position update {updated.Symbol} {updated.Side} qty={updated.Quantity} (Notional={updated.NotionalUsdt} USDT)"); } catch { }
                try { PositionsChanged?.Invoke(this, EventArgs.Empty); } catch { }
            }
        }

        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(string? symbol, CancellationToken ct = default)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(symbol)) return Task.FromResult((IReadOnlyList<OrderDto>)_openOrders.ToList());
                var list = _openOrders.Where(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList();
                return Task.FromResult((IReadOnlyList<OrderDto>)list);
            }
        }

        public Task<IReadOnlyList<TradeRecord>> GetRecentTradesAsync(DateTime from, DateTime to, string? symbol, CancellationToken ct = default)
        {
            lock (_sync)
            {
                var q = _recentTrades.Where(t => t.CloseTime >= from && t.CloseTime <= to);
                if (!string.IsNullOrWhiteSpace(symbol)) q = q.Where(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult((IReadOnlyList<TradeRecord>)q.ToList());
            }
        }

        public Task<IReadOnlyList<DailyPnlRow>> GetDailyPnlAsync(DateTime from, DateTime to, string? symbol, CancellationToken ct = default)
        {
            lock (_sync)
            {
                var q = _dailyPnl.Where(d => d.Date >= from.Date && d.Date <= to.Date);
                if (!string.IsNullOrWhiteSpace(symbol)) q = q.Where(d => string.Equals(d.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult((IReadOnlyList<DailyPnlRow>)q.ToList());
            }
        }

        public Task RefreshPositionsAsync(CancellationToken ct = default)
        {
            return ReconcileOnceAsync(ct);
        }

        public Task PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct = default)
        {
            return _adapter.PlaceOrderAsync(req.Symbol, req.Side, req.Quantity, req.Reason, ct);
        }

        public Task ClosePositionAsync(ClosePositionRequest req, CancellationToken ct = default)
        {
            return _adapter.ClosePositionAsync(req.Symbol, req.Reason, ct);
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _bgTask?.Wait(1000); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _adapter.PositionChanged -= OnAdapterPositionChanged; } catch { }
        }

        private sealed class PositionDtoComparer : IEqualityComparer<PositionDto>
        {
            public bool Equals(PositionDto? x, PositionDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return string.Equals(x.Symbol, y.Symbol, StringComparison.OrdinalIgnoreCase)
                       && x.Side == y.Side
                       && x.Quantity == y.Quantity
                       && x.EntryPrice == y.EntryPrice;
            }

            public int GetHashCode(PositionDto obj)
            {
                return HashCode.Combine(obj.Symbol?.ToUpperInvariant(), obj.Side, obj.Quantity, obj.EntryPrice);
            }
        }
    }
}
