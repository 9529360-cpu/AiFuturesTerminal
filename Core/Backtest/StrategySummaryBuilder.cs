using System;
using System.Collections.Generic;
using System.Linq;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Backtest.Models;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.Core.Backtest
{
    public static class StrategySummaryBuilder
    {
        public static BacktestSummary BuildSummary(string strategyName, IReadOnlyList<TradeRecord> trades, decimal initialEquity)
        {
            var list = (trades ?? Array.Empty<TradeRecord>()).OrderBy(t => t.CloseTime).ToList();

            var netPnl = list.Sum(t => t.RealizedPnl);
            var grossProfit = list.Where(t => t.RealizedPnl > 0m).Sum(t => t.RealizedPnl);
            var grossLoss = list.Where(t => t.RealizedPnl < 0m).Sum(t => t.RealizedPnl);

            var totalTrades = list.Count;
            var wins = list.Count(t => t.RealizedPnl > 0m);
            var losses = list.Count(t => t.RealizedPnl < 0m);

            var winRate = totalTrades > 0 ? (double)wins / totalTrades : 0.0;

            // avgR placeholder: average PnL per trade
            var avgPnl = totalTrades > 0 ? netPnl / totalTrades : 0m;
            var avgR = avgPnl;

            // profit factor
            decimal profitFactor;
            var absGrossLoss = Math.Abs(grossLoss);
            if (absGrossLoss == 0m)
            {
                profitFactor = grossProfit > 0m ? decimal.MaxValue : 0m;
            }
            else
            {
                profitFactor = grossProfit / absGrossLoss;
            }

            // max drawdown from equity sequence
            decimal maxDrawdown = CalculateMaxDrawdown(list, initialEquity);

            // sanity checks and warnings
            if (Math.Abs(netPnl) > 1_000_000_000m || profitFactor > 1000m || Math.Abs(avgR) > 1000m)
            {
                try { Console.WriteLine($"[Backtest] Warning: suspicious summary for strategy {strategyName}: NetPnl={netPnl}, ProfitFactor={profitFactor}, AvgR={avgR}"); } catch { }
            }

            return new BacktestSummary(netPnl, maxDrawdown, totalTrades, winRate, grossProfit, grossLoss, avgR, profitFactor);
        }

        public static Models.StrategyComparisonRow BuildComparisonRow(Core.Strategy.StrategyKind strategy, IReadOnlyList<TradeRecord> trades, decimal initialEquity)
        {
            var summary = BuildSummary(strategy.ToString(), trades, initialEquity);
            return new Models.StrategyComparisonRow(strategy, summary);
        }

        /// <summary>
        /// Build comparison rows by grouping given trades by their StrategyName.
        /// StrategyName must match the StrategyKind enum name (e.g. "ScalpingMomentum").
        /// </summary>
        public static IReadOnlyList<Models.StrategyComparisonRow> BuildComparisonRowsFromTrades(IReadOnlyList<TradeRecord> trades, decimal initialEquity)
        {
            var rows = new List<Models.StrategyComparisonRow>();
            if (trades == null || trades.Count == 0) return rows;

            var groups = trades.GroupBy(t => t.StrategyName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (string.IsNullOrWhiteSpace(g.Key)) continue;
                if (Enum.TryParse<StrategyKind>(g.Key, true, out var kind))
                {
                    rows.Add(BuildComparisonRow(kind, g.ToList(), initialEquity));
                }
                else
                {
                    try { Console.WriteLine($"[Backtest] Warning: unknown strategy name in trades grouping: '{g.Key}'"); } catch { }
                }
            }

            return rows;
        }

        private static decimal CalculateMaxDrawdown(IReadOnlyList<TradeRecord> orderedTrades, decimal initialEquity)
        {
            decimal equity = initialEquity;
            decimal peakEquity = initialEquity;
            decimal maxDd = 0m;

            foreach (var trade in orderedTrades)
            {
                equity += trade.RealizedPnl;
                if (equity > peakEquity) peakEquity = equity;
                var dd = peakEquity - equity;
                if (dd > maxDd) maxDd = dd;
            }

            return maxDd;
        }
    }
}
