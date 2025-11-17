using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiFuturesTerminal.Core.Analytics;

public sealed class InMemoryTradeBook : ITradeBook
{
    private readonly List<TradeRecord> _trades = new();
    private readonly object _sync = new();

    public event EventHandler<TradeRecord>? TradeRecorded;

    public void AddTrade(TradeRecord trade)
    {
        lock (_sync)
        {
            _trades.Add(trade);
        }

        TradeRecorded?.Invoke(this, trade);
    }

    public Task AddAsync(TradeRecord trade, CancellationToken ct = default)
    {
        AddTrade(trade);
        return Task.CompletedTask;
    }

    public IReadOnlyList<TradeRecord> GetTrades(DateOnly date)
    {
        lock (_sync)
        {
            return _trades
                .Where(t => DateOnly.FromDateTime(t.CloseTime) == date)
                .ToList();
        }
    }

    public Task<IReadOnlyList<TradeRecord>> GetTradesAsync(DateTime? from = null, DateTime? to = null, string? strategyName = null, CancellationToken ct = default)
    {
        IReadOnlyList<TradeRecord> res;
        lock (_sync)
        {
            var q = _trades.AsEnumerable();
            if (from.HasValue) q = q.Where(t => t.CloseTime >= from.Value);
            if (to.HasValue) q = q.Where(t => t.CloseTime <= to.Value);
            if (!string.IsNullOrEmpty(strategyName)) q = q.Where(t => string.Equals(t.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase));
            res = q.ToList();
        }
        return Task.FromResult(res);
    }

    public DailyTradeSummary GetDailySummary(DateOnly date)
    {
        var trades = GetTrades(date);

        var totalPnL = trades.Sum(t => t.RealizedPnl);
        var winCount = trades.Count(t => t.RealizedPnl > 0);
        var loseCount = trades.Count(t => t.RealizedPnl < 0);

        // 先简化：MaxDrawdown 暂时用累积 PnL 的最小值估算
        decimal running = 0m;
        decimal minEquity = 0m;
        foreach (var t in trades.OrderBy(t => t.CloseTime))
        {
            running += t.RealizedPnl;
            if (running < minEquity) minEquity = running;
        }

        return new DailyTradeSummary
        {
            TradingDate = date,
            TradeCount = trades.Count,
            WinCount = winCount,
            LoseCount = loseCount,
            TotalPnL = totalPnL,
            MaxDrawdown = minEquity
        };
    }

    public IReadOnlyList<TradeRecord> GetAllTrades()
    {
        lock (_sync)
        {
            return _trades.ToList();
        }
    }

    public Task<IReadOnlyList<TradeRecord>> GetAllTradesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TradeRecord> res;
        lock (_sync)
        {
            res = _trades.ToList();
        }
        return Task.FromResult(res);
    }
}
