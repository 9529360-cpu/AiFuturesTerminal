namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.Linq;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

public sealed class RangeMeanReversionStrategy : IStrategy
{
    private readonly StrategyConfig _config;

    public RangeMeanReversionStrategy(StrategyConfig config)
    {
        _config = config;
    }

    public string Name => nameof(RangeMeanReversionStrategy);

    public ExecutionDecision OnBar(StrategyContext context)
    {
        var history = context.History ?? Array.Empty<Candle>();
        var current = context.CurrentBar;
        int period = Math.Max(5, _config.RangePeriod);
        if (history.Count < period) return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());

        var window = history.Skip(Math.Max(0, history.Count - period)).ToList();
        var mean = window.Average(h => h.Close);
        var variance = window.Average(h => (h.Close - mean) * (h.Close - mean));
        var std = (decimal)Math.Sqrt((double)variance);
        var upper = mean + _config.RangeBandWidth * std;
        var lower = mean - _config.RangeBandWidth * std;

        // RSI helper
        decimal Rsi(IReadOnlyList<Candle> src, int len)
        {
            if (src == null || src.Count < len + 1) return 50m;
            var gains = 0m; var losses = 0m;
            for (int i = src.Count - len; i < src.Count; i++)
            {
                var diff = src[i].Close - src[i - 1].Close;
                if (diff > 0) gains += diff; else losses -= diff;
            }
            if (gains + losses == 0) return 50m;
            var rs = gains / Math.Max(1m, losses);
            return 100m - (100m / (1m + rs));
        }

        var rsi = Rsi(history.Concat(new[] { current }).ToList(), 14);
        var price = current.Close;
        var pos = context.CurrentPosition;
        bool flat = pos == null || pos.IsFlat();

        // long when touch lower and RSI oversold
        if (flat && price <= lower && rsi < 30m)
        {
            var entry = price;
            var tp = mean;
            var sl = entry - std * 1.5m;
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenLong,
                Symbol = current.Symbol,
                Reason = "range_long",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = sl,
                TakeProfitPrice = tp,
                StrategyName = _config.Kind.ToString()
            };
        }

        // short when touch upper and RSI overbought
        if (flat && price >= upper && rsi > 70m)
        {
            var entry = price;
            var tp = mean;
            var sl = entry + std * 1.5m;
            return new ExecutionDecision
            {
                Type = ExecutionDecisionType.OpenShort,
                Symbol = current.Symbol,
                Reason = "range_short",
                EntryPrice = entry,
                LastPrice = price,
                StopLossPrice = sl,
                TakeProfitPrice = tp,
                StrategyName = _config.Kind.ToString()
            };
        }

        if (!flat && pos != null)
        {
            // close near mean
            if ((pos.Side == PositionSide.Long && price >= mean) || (pos.Side == PositionSide.Short && price <= mean))
            {
                return new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = current.Symbol, Reason = "range_close_mean", LastPrice = price, StrategyName = _config.Kind.ToString() };
            }
        }

        return ExecutionDecision.None(current.Symbol, strategyName: _config.Kind.ToString());
    }
}
