namespace AiFuturesTerminal.Core.Exchanges.Mock;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// 本地 Mock 交易所实现，用于本地开发与演示。生成假 K 线并维护简单的账户/持仓状态。
/// 注意：这是一个近似模拟器，不用于真实交易，仅用于开发/测试。
/// TODO: 后续可以替换为真实交易所适配器（Binance 等）。
/// </summary>
public sealed class MockExchangeAdapter : IExchangeAdapter
{
    private readonly Random _rnd = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly Dictionary<string, decimal> _lastPrice = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lastPriceLock = new();
    private decimal _equity = 1000m;
    private decimal _freeBalance = 1000m;

    /// <summary>
    /// 获取历史 K 线，按时间正序返回。
    /// 同时会更新内部记录的最近成交价（使用最后一根 K 线的 Close）。
    /// </summary>
    public Task<IReadOnlyList<Candle>> GetHistoricalCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) return Task.FromResult((IReadOnlyList<Candle>)Array.Empty<Candle>());

        symbol = symbol.ToUpperInvariant();
        var now = DateTime.UtcNow;
        var candles = new List<Candle>(limit);
        decimal basePrice = 100m + (_rnd.NextDecimal() - 0.5m) * 10m; // around 100

        for (int i = limit - 1; i >= 0; i--)
        {
            var time = now - TimeSpan.FromTicks(interval.Ticks * i);
            // random walk
            var change = (decimal)(_rnd.NextDouble() - 0.5) * 2m;
            basePrice = Math.Max(0.01m, basePrice + change);
            var open = basePrice;
            var close = Math.Max(0.01m, basePrice + (decimal)(_rnd.NextDouble() - 0.5));
            var high = Math.Max(open, close) + 0.1m;
            var low = Math.Min(open, close) - 0.1m;
            var volume = _rnd.Next(100, 1000);

            candles.Add(new Candle
            {
                Symbol = symbol,
                OpenTime = time - interval,
                CloseTime = time,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        // ensure ascending by CloseTime
        var ordered = candles.OrderBy(c => c.CloseTime).ToList();

        // update last price for this symbol using the last candle's Close
        if (ordered.Count > 0)
        {
            var last = ordered.Last().Close;
            lock (_lastPriceLock)
            {
                _lastPrice[symbol] = last;
            }
        }

        return Task.FromResult((IReadOnlyList<Candle>)ordered);
    }

    /// <summary>
    /// 返回账户快照（权益、可用余额等）。
    /// </summary>
    public Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new AccountSnapshot(_equity, _freeBalance, DateTime.UtcNow));
    }

    /// <summary>
    /// 获取指定交易对的当前持仓（如果有），不存在则返回 null。
    /// </summary>
    public Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        if (_positions.TryGetValue(symbol, out var pos) && !pos.IsFlat())
        {
            return Task.FromResult<Position?>(pos);
        }

        return Task.FromResult<Position?>(null);
    }

    /// <summary>
    /// 下单（简化模型）：认为以当前市价立即成交。市价来源为最近生成的 K 线 Close；若不存在则使用兜底价格（100m）。
    /// TODO: 这是一个简化假设，未来可模拟撮合与滑点。
    /// </summary>
    public Task PlaceOrderAsync(string symbol, PositionSide side, decimal quantity, string reason, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var pos = _positions.GetOrAdd(symbol, _ => new Position(symbol));
        lock (pos)
        {
            if (!pos.IsFlat())
            {
                // already have position
                throw new InvalidOperationException("already_have_position: " + symbol + " " + reason);
            }

            pos.Symbol = symbol;
            pos.Side = side;
            pos.Quantity = quantity;

            // use last price if available, otherwise fallback to 100m
            decimal lastPrice;
            lock (_lastPriceLock)
            {
                if (!_lastPrice.TryGetValue(symbol, out lastPrice))
                {
                    // fallback default price when no historical candles were requested yet
                    lastPrice = 100m; // TODO: 兜底价格，真实场景应由行情决定
                }
            }

            try { Console.WriteLine($"[MockAdapter] PlaceOrder {symbol} qty={quantity}, adapterLastPrice={lastPrice}, reason={reason}"); } catch { }

            pos.EntryPrice = lastPrice;
            pos.EntryTime = DateTime.UtcNow;
            _positions[symbol] = pos;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 平仓（简化模型）：按当前市价（最近 K 线 Close 或兜底价）计算 PnL，并更新账户权益与可用余额。
    /// </summary>
    public Task ClosePositionAsync(string symbol, string reason, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        if (_positions.TryGetValue(symbol, out var pos) && !pos.IsFlat())
        {
            lock (pos)
            {
                decimal lastPrice;
                lock (_lastPriceLock)
                {
                    if (!_lastPrice.TryGetValue(symbol, out lastPrice))
                    {
                        lastPrice = 100m; // TODO: 兜底价格
                    }
                }

                var pnl = pos.GetUnrealizedPnl(lastPrice);
                _equity += pnl;
                _freeBalance = _equity;

                // reset position
                _positions[symbol] = new Position(symbol);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 外部可用接口：设置/更新某 symbol 的最近成交价（用于回测期间与市场数据同步）。
    /// </summary>
    public void SetLastPrice(string symbol, decimal price)
    {
        if (string.IsNullOrEmpty(symbol)) return;
        symbol = symbol.ToUpperInvariant();
        lock (_lastPriceLock)
        {
            _lastPrice[symbol] = price;
        }
    }
}

internal static class RandomExtensions
{
    public static decimal NextDecimal(this Random rnd)
    {
        return (decimal)rnd.NextDouble();
    }
}
