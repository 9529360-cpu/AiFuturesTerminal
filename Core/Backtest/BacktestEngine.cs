using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Environment;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.Core.Backtest;

/// <summary>
/// 回测中的一笔成交记录。
/// </summary>
public sealed class BacktestTrade
{
    public string Symbol { get; init; } = string.Empty;
    public DateTime EntryTime { get; init; }
    public DateTime ExitTime { get; init; }
    public PositionSide Side { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal Pnl { get; init; }
}

/// <summary>
/// 权益曲线点。
/// </summary>
public sealed class EquityPoint
{
    public DateTime Time { get; init; }
    public decimal Equity { get; init; }
}

/// <summary>
/// 回测结果摘要。
/// </summary>
public sealed class BacktestResult
{
    public string Symbol { get; init; } = string.Empty;
    public string StrategyId { get; init; } = string.Empty;
    public decimal InitialEquity { get; init; }
    public decimal FinalEquity { get; init; }
    public IReadOnlyList<BacktestTrade> Trades { get; init; } = Array.Empty<BacktestTrade>();
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = Array.Empty<EquityPoint>();
}

/// <summary>
/// 简单回测引擎：单品种、单仓位模拟，使用策略委托决定每根 K 线是否开/平仓。
/// 该实现用于验证流程并能快速运行，不考虑手续费、滑点或复杂撮合。
/// 重构后基于 ITradingEnvironment 和 IStrategy / StrategyContext 驱动。
/// </summary>
public sealed class BacktestEngine
{
    private readonly ExecutionEngine _executionEngine;

    // Hook that can be set by composition root to persist backtest results without direct DI coupling
    public static Func<BacktestResult, Task>? BacktestResultPersistHook;

    public BacktestEngine(ExecutionEngine executionEngine)
    {
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
    }

    /// <summary>
    /// 旧的 delegate 版本，保持兼容性。
    /// </summary>
    public async Task<BacktestResult> RunSingleSymbolAsync(string symbol, IReadOnlyList<Candle> candles, Func<IReadOnlyList<Candle>, Position, ExecutionDecision> strategy, decimal initialEquity, CancellationToken ct = default)
    {
        if (strategy == null) throw new ArgumentNullException(nameof(strategy));

        // wrap delegate and call new overload
        var adapter = new DelegateStrategyAdapter(strategy);
        return await RunSingleSymbolAsync(adapter, symbol, candles, initialEquity, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 新增：接受 IStrategy 的重载，回测循环直接调用 strategy.OnBar(context)
    /// </summary>
    public async Task<BacktestResult> RunSingleSymbolAsync(IStrategy strategy, string symbol, IReadOnlyList<Candle> candles, decimal initialEquity, CancellationToken ct = default)
    {
        if (strategy == null) throw new ArgumentNullException(nameof(strategy));
        if (candles == null || candles.Count == 0) throw new ArgumentException("candles 不能为空", nameof(candles));

        // Create backtest environment with initial time
        var env = new BacktestTradingEnvironment(candles.First().CloseTime);

        decimal equity = initialEquity;
        var equityCurve = new List<EquityPoint>();

        var currentPosition = new Position(symbol);

        var startIndex = Math.Min(30, candles.Count - 1);

        for (int i = startIndex; i < candles.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var history = candles.Take(i + 1).ToList();
            var current = history.Last();

            env.AdvanceTo(current.CloseTime);

            // keep mock adapter price in sync with current bar when running backtest environment
            try
            {
                if (env is BacktestTradingEnvironment bEnv)
                {
                    bEnv.UpdateMarketPrice(current.Symbol, current.Close);
                }
            }
            catch { }

            var ctx = new StrategyContext
            {
                Now = env.UtcNow,
                CurrentBar = current,
                History = history,
                CurrentPosition = currentPosition.IsFlat() ? null : currentPosition,
                Account = await env.GetAccountSnapshotAsync(ct).ConfigureAwait(false),
            };

            var decision = strategy.OnBar(ctx);

            var execResult = await _executionEngine.ExecuteAsync(decision, env, ct).ConfigureAwait(false);

            // recompute equity from tradebook
            var trades = env.TradeBook.GetAllTrades();
            var realized = trades.Sum(t => t.RealizedPnl);
            equity = initialEquity + realized;
            equityCurve.Add(new EquityPoint { Time = current.CloseTime, Equity = equity });

            var posSnapshot = await env.GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);
            if (posSnapshot != null && !posSnapshot.IsFlat())
            {
                currentPosition = posSnapshot;
            }
            else
            {
                currentPosition = new Position(symbol);
            }
        }

        var result = new BacktestResult
        {
            Symbol = symbol,
            StrategyId = strategy.Name,
            InitialEquity = initialEquity,
            FinalEquity = equity,
            Trades = env.TradeBook.GetAllTrades().Select(t => new BacktestTrade
            {
                Symbol = t.Symbol,
                EntryTime = t.OpenTime,
                ExitTime = t.CloseTime,
                Side = t.Side == TradeSide.Long ? PositionSide.Long : PositionSide.Short,
                EntryPrice = t.EntryPrice,
                ExitPrice = t.ExitPrice,
                Quantity = t.Quantity,
                Pnl = t.RealizedPnl
            }).ToList(),
            EquityCurve = equityCurve
        };

        // Log counts for diagnostics
        try
        {
            Console.WriteLine($"[BacktestEngine] Result for {symbol}: trades={result.Trades.Count}, equityPoints={result.EquityCurve.Count}");
        }
        catch { }

        // Persist backtest result to history store if enabled via hook
        try
        {
            if (BacktestResultPersistHook != null)
            {
                // fire and forget
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await BacktestResultPersistHook(result).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        try { Console.WriteLine($"Backtest persist hook error: {ex.Message}"); } catch { }
                    }
                });
            }
        }
        catch { }

        return result;
    }

    // Adapter to wrap existing delegate-based strategy into IStrategy
    private sealed class DelegateStrategyAdapter : AiFuturesTerminal.Core.Strategy.IStrategy
    {
        private readonly Func<IReadOnlyList<Candle>, Position, ExecutionDecision> _del;

        public DelegateStrategyAdapter(Func<IReadOnlyList<Candle>, Position, ExecutionDecision> del)
        {
            _del = del ?? throw new ArgumentNullException(nameof(del));
        }

        public string Name => "DelegateAdapter";

        public ExecutionDecision OnBar(AiFuturesTerminal.Core.Strategy.StrategyContext context)
        {
            var history = context.History ?? Array.Empty<Candle>();
            var position = context.CurrentPosition ?? new Position(context.CurrentBar.Symbol);
            var decision = _del(history, position) ?? ExecutionDecision.None(context.CurrentBar.Symbol);

            // Ensure decision has sensible price values to avoid risk sizing failures
            var barPrice = context.CurrentBar.Close;
            if ((decision.Type == ExecutionDecisionType.OpenLong || decision.Type == ExecutionDecisionType.OpenShort) && (decision.EntryPrice == null || decision.EntryPrice <= 0m))
            {
                decision = decision with { EntryPrice = barPrice };
            }

            if ((decision.Type == ExecutionDecisionType.Close || decision.Type == ExecutionDecisionType.OpenLong || decision.Type == ExecutionDecisionType.OpenShort) && (decision.LastPrice == null || decision.LastPrice <= 0m))
            {
                decision = decision with { LastPrice = barPrice };
            }

            // ensure delegate decisions carry a strategy identifier
            if (string.IsNullOrWhiteSpace(decision.StrategyName))
            {
                decision = decision with { StrategyName = Name };
            }

            return decision;
        }
    }
}
