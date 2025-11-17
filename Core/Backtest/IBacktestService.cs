using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.Core.Backtest
{
    public sealed record BacktestRequest(
        string Symbol,
        StrategyKind Strategy,
        DateTime StartTime,
        DateTime EndTime,
        StrategyConfig Config
    );

    public sealed record BacktestSummary(
        decimal NetPnl,
        decimal MaxDrawdown,
        int TradeCount,
        double WinRate,
        decimal GrossProfit,
        decimal GrossLoss,
        decimal AvgR,
        decimal ProfitFactor
    );

    public sealed record BacktestServiceResult(
        BacktestSummary Summary,
        IReadOnlyList<TradeRecord> Trades
    );

    public sealed record StrategyComparisonRow(
        StrategyKind Strategy,
        BacktestSummary Summary
    );

    public interface IBacktestService
    {
        // Event for publishing log messages from backtest service to UI or other subscribers
        event Action<string>? Log;

        Task<BacktestServiceResult> RunAsync(BacktestRequest request, CancellationToken ct = default);
    }
}
