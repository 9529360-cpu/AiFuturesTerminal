namespace AiFuturesTerminal.Core.Backtest;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Environment;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Analytics;
using System.Text;

/// <summary>
/// Helper to compare BacktestEngine results with running the same strategy through ExecutionEngine + BacktestTradingEnvironment (Mock router).
/// Intended for developer/debug use.
/// </summary>
public static class BacktestComparer
{
    public static async Task<string> CompareAsync(BacktestEngine backtestEngine, ExecutionEngine execEngine, IStrategy strategy, string symbol, IReadOnlyList<Candle> candles, decimal initialEquity, CancellationToken ct = default)
    {
        if (backtestEngine == null) throw new ArgumentNullException(nameof(backtestEngine));
        if (execEngine == null) throw new ArgumentNullException(nameof(execEngine));
        if (strategy == null) throw new ArgumentNullException(nameof(strategy));
        if (candles == null || candles.Count == 0) throw new ArgumentException("candles required", nameof(candles));

        // Run backtest
        var btResult = await backtestEngine.RunSingleSymbolAsync(strategy, symbol, candles, initialEquity, ct).ConfigureAwait(false);

        // Run execution-engine-based simulation: reuse BacktestTradingEnvironment so OrderRouter is Mock and TradeBook is in-memory
        var env = new BacktestTradingEnvironment(candles.First().CloseTime);

        // ensure ExecutionEngine will treat this run as Backtest/DryRun when recording trades: caller should configure execEngine's AppEnvironmentOptions accordingly

        var position = new Position(symbol);

        for (int i = Math.Min(30, candles.Count - 1); i < candles.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var history = candles.Take(i + 1).ToList();
            var current = history.Last();
            env.AdvanceTo(current.CloseTime);
            env.UpdateMarketPrice(current.Symbol, current.Close);

            var ctx = new StrategyContext
            {
                Now = env.UtcNow,
                CurrentBar = current,
                History = history,
                CurrentPosition = position.IsFlat() ? null : position,
                Account = await env.GetAccountSnapshotAsync(ct).ConfigureAwait(false)
            };

            var decision = strategy.OnBar(ctx);
            await execEngine.ExecuteAsync(decision, env, ct).ConfigureAwait(false);

            var posSnapshot = await env.GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);
            position = posSnapshot != null && !posSnapshot.IsFlat() ? posSnapshot : new Position(symbol);
        }

        // Collect results
        var execTrades = env.TradeBook.GetAllTrades();

        // Write comparison to temp file
        var sb = new StringBuilder();
        sb.AppendLine("BacktestResult: ");
        sb.AppendLine($"Symbol={btResult.Symbol}, Strategy={btResult.StrategyId}, InitialEquity={btResult.InitialEquity}, FinalEquity={btResult.FinalEquity}, Trades={btResult.Trades.Count}");
        sb.AppendLine();
        sb.AppendLine("Backtest Trades:");
        foreach (var t in btResult.Trades)
        {
            sb.AppendLine($"{t.EntryTime:O},{t.ExitTime:O},{t.Side},{t.EntryPrice},{t.ExitPrice},{t.Quantity},{t.Pnl}");
        }

        sb.AppendLine();
        sb.AppendLine("ExecutionEngine (Mock) Trades:");
        foreach (var t in execTrades)
        {
            sb.AppendLine($"{t.OpenTime:O},{t.CloseTime:O},{t.Side},{t.EntryPrice},{t.ExitPrice},{t.Quantity},{t.RealizedPnl}");
        }

        var outPath = Path.Combine(Path.GetTempPath(), $"backtest_compare_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outPath, sb.ToString(), ct).ConfigureAwait(false);

        return outPath;
    }
}
