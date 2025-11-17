namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.Linq;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

public sealed class ScalpingMomentumStrategy : IStrategy
{
    private readonly StrategyConfig _config;

    public ScalpingMomentumStrategy(StrategyConfig config)
    {
        _config = config;
    }

    public string Name => nameof(ScalpingMomentumStrategy);

    public ExecutionDecision OnBar(StrategyContext context)
    {
        var history = context.History ?? Array.Empty<Candle>();
        var current = context.CurrentBar;
        if (string.IsNullOrEmpty(current.Symbol)) return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());

        // build series including current bar
        var series = history.Concat(new[] { current }).ToList();
        if (series.Count < 3) return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());

        // EMA helpers
        decimal Ema(IReadOnlyList<Candle> src, int length)
        {
            if (src == null || src.Count == 0) return 0m;
            int count = src.Count;
            int start = Math.Max(0, count - length);
            decimal sma = src.Skip(start).Take(length).Average(s => s.Close);
            decimal ema = sma;
            var alpha = 2m / (length + 1m);
            for (int i = start + length; i < count; i++)
            {
                ema = (src[i].Close - ema) * alpha + ema;
            }
            return ema;
        }

        // compute EMAs: 3 and 9
        var lenFast = 3;
        var lenSlow = 9;
        var fastEma = Ema(series, Math.Min(lenFast, series.Count));
        var slowEma = Ema(series, Math.Min(lenSlow, series.Count));

        // recent N bar percent change sum
        int recentN = Math.Min(5, series.Count - 1);
        decimal pctSum = 0m;
        for (int i = series.Count - recentN; i < series.Count; i++)
        {
            if (i <= 0) continue;
            var prev = series[i - 1].Close;
            if (prev == 0) continue;
            pctSum += (series[i].Close - prev) / prev;
        }

        var threshold = 0.001m; // 0.1%
        var price = current.Close;

        // position logic
        var pos = context.CurrentPosition;
        bool flat = pos == null || pos.IsFlat();

        // Entry long
        if (flat && fastEma > slowEma && pctSum > threshold)
        {
            var entry = price;
            var sl = entry * (1m - 0.003m); // 0.3%
            var tp = entry * (1m + 0.005m); // 0.5%
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenLong,
                Symbol = current.Symbol,
                Reason = "scalp_momentum_open",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = sl,
                TakeProfitPrice = tp,
                StrategyName = _config.Kind.ToString()
            };
        }

        // Entry short
        if (flat && fastEma < slowEma && pctSum < -threshold)
        {
            var entry = price;
            var sl = entry * (1m + 0.003m); // 0.3% above
            var tp = entry * (1m - 0.005m);
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenShort,
                Symbol = current.Symbol,
                Reason = "scalp_momentum_open_short",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = sl,
                TakeProfitPrice = tp,
                StrategyName = _config.Kind.ToString()
            };
        }

        // Exit conditions if holding
        if (!flat && pos != null)
        {
            // use last price
            var lastPrice = price;
            var unreal = pos.GetUnrealizedPnl(lastPrice);

            // if ema crossed against us or takeprofit/stoploss levels reached, close
            if ((pos.Side == PositionSide.Long && (fastEma < slowEma || lastPrice <= pos.EntryPrice * (1m - 0.003m) || lastPrice >= pos.EntryPrice * (1m + 0.005m))) ||
                (pos.Side == PositionSide.Short && (fastEma > slowEma || lastPrice >= pos.EntryPrice * (1m + 0.003m) || lastPrice <= pos.EntryPrice * (1m - 0.005m))))
            {
                return new ExecutionDecision
                {
                    Type = ExecutionDecisionType.Close,
                    Symbol = current.Symbol,
                    Reason = "scalp_momentum_close",
                    LastPrice = lastPrice,
                    StrategyName = _config.Kind.ToString()
                };
            }
        }

        return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());
    }
}
