namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.Linq;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

public sealed class TrendFollowingStrategy : IStrategy
{
    private readonly StrategyConfig _config;

    public TrendFollowingStrategy(StrategyConfig config)
    {
        _config = config;
    }

    public string Name => nameof(TrendFollowingStrategy);

    public ExecutionDecision OnBar(StrategyContext context)
    {
        var history = context.History ?? Array.Empty<Candle>();
        var current = context.CurrentBar;
        if (history.Count < 20) return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());

        // simple EMA helper
        decimal Ema(IReadOnlyList<Candle> src, int length)
        {
            if (src == null || src.Count == 0) return 0m;
            int count = src.Count;
            int start = Math.Max(0, count - length);
            decimal sma = src.Skip(start).Take(length).Average(s => s.Close);
            decimal ema = sma;
            var alpha = 2m / (length + 1m);
            for (int i = start + length; i < count; i++) ema = (src[i].Close - ema) * alpha + ema;
            return ema;
        }

        // ATR helper
        decimal Atr(IReadOnlyList<Candle> src, int length)
        {
            if (src == null || src.Count < 2) return 0m;
            var trs = new List<decimal>();
            for (int i = 1; i < src.Count; i++)
            {
                var high = src[i].High;
                var low = src[i].Low;
                var prevClose = src[i - 1].Close;
                trs.Add(Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose))));
            }
            var take = Math.Min(trs.Count, length);
            return trs.Skip(Math.Max(0, trs.Count - take)).Average();
        }

        var fast = Ema(history.Concat(new[] { current }).ToList(), _config.TrendFastMaLength);
        var slow = Ema(history.Concat(new[] { current }).ToList(), _config.TrendSlowMaLength);
        var price = current.Close;

        var pos = context.CurrentPosition;
        bool flat = pos == null || pos.IsFlat();

        var atr = Atr(history.Concat(new[] { current }).ToList(), 14);
        var slDist = atr * 2m;
        var tpDist = atr * 4m;

        // Entry long
        if (flat && fast > slow && price > slow && price > fast)
        {
            var entry = price;
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenLong,
                Symbol = current.Symbol,
                Reason = "trend_open",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = entry - slDist,
                TakeProfitPrice = entry + tpDist,
                StrategyName = _config.Kind.ToString()
            };
        }

        // Entry short
        if (flat && fast < slow && price < slow && price < fast)
        {
            var entry = price;
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenShort,
                Symbol = current.Symbol,
                Reason = "trend_open_short",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = entry + slDist,
                TakeProfitPrice = entry - tpDist,
                StrategyName = _config.Kind.ToString()
            };
        }

        // Exit logic
        if (!flat && pos != null)
        {
            var lastPrice = price;
            // if cross against trend or ATR stop hit, close
            if ((pos.Side == PositionSide.Long && (fast < slow || lastPrice <= pos.EntryPrice - slDist || lastPrice >= pos.EntryPrice + tpDist)) ||
                (pos.Side == PositionSide.Short && (fast > slow || lastPrice >= pos.EntryPrice + slDist || lastPrice <= pos.EntryPrice - tpDist)))
            {
                return new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = current.Symbol, Reason = "trend_close", LastPrice = lastPrice, StrategyName = _config.Kind.ToString() };
            }
        }

        return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());
    }
}
