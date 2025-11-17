using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.MarketData;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.Core.Backtest
{
    public sealed class BacktestService : IBacktestService
    {
        private readonly BacktestEngine _engine;
        private readonly MarketDataService _marketData;
        private readonly IStrategyFactory _strategyFactory;

        // optional logger delegate (legacy)
        private readonly Action<string>? _logger;

        // public event for subscribers to receive log messages
        public event Action<string>? Log;

        public BacktestService(BacktestEngine engine, MarketDataService marketData, IStrategyFactory strategyFactory, Action<string>? logger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
            _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
            _logger = logger;
        }

        private void PublishLog(string message)
        {
            try
            {
                _logger?.Invoke(message);
            }
            catch { }
            try
            {
                Log?.Invoke(message);
            }
            catch { }
        }

        public async Task<BacktestServiceResult> RunAsync(BacktestRequest request, CancellationToken ct = default)
        {
            PublishLog($"[回测] 开始: 标的={request.Symbol}, 策略={request.Strategy}, 开始={request.StartTime}, 结束={request.EndTime}");

            // determine a safe limit for exchange API (Binance max is typically 1500)
            const int MaxBinanceKlineLimit = 1500;
            var requestedSpanMinutes = (int)Math.Ceiling((request.EndTime - request.StartTime).TotalMinutes);
            var limit = Math.Clamp(requestedSpanMinutes > 0 ? requestedSpanMinutes : 1, 1, MaxBinanceKlineLimit);

            List<Candle> candles;
            try
            {
                // load candles from market data service with safe limit
                candles = (await _marketData.LoadHistoricalCandlesAsync(request.Symbol, TimeSpan.FromMinutes(1), limit, ct).ConfigureAwait(false)).ToList();
            }
            catch (OperationCanceledException)
            {
                PublishLog($"[回测] 获取 K 线被取消: {request.Symbol}");
                throw;
            }
            catch (Exception ex)
            {
                PublishLog($"[回测] 获取 K 线失败: {ex.Message} (limit={limit})");
                throw;
            }

            PublishLog($"[回测] 已获取 K 线 {candles.Count} 条，Symbol={request.Symbol}, Start={request.StartTime}, End={request.EndTime}, limit={limit}");

            // Ensure the strategy kind in the provided config matches the requested strategy
            try
            {
                // if caller passed a shared StrategyConfig, update its Kind to the requested strategy so factory creates the intended implementation
                request.Config.Kind = request.Strategy;
            }
            catch { }

            // Create strategy instance using factory
            var strategy = _strategyFactory.Create(request.Config);

            BacktestResult result;
            try
            {
                result = await _engine.RunSingleSymbolAsync(strategy, request.Symbol, candles, initialEquity: 1000m, ct: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                PublishLog($"[回测] 运行中取消: Symbol={request.Symbol}, Strategy={request.Strategy}");
                throw;
            }
            catch (Exception ex)
            {
                PublishLog($"[回测] 引擎错误: {ex.Message}");
                throw;
            }

            // note: BacktestEngine returns BacktestResult (with Trades and EquityCurve)
            PublishLog($"[回测] 完成: Trades={result.Trades.Count} 笔, 最终权益={result.FinalEquity}");

            // map BacktestEngine.BacktestResult to our service result (convert BacktestTrade -> TradeRecord)
            var trades = result.Trades.Select(t => new TradeRecord
            {
                OpenTime = t.EntryTime,
                CloseTime = t.ExitTime,
                Symbol = t.Symbol,
                Side = t.Side == PositionSide.Long ? TradeSide.Long : TradeSide.Short,
                Quantity = t.Quantity,
                EntryPrice = t.EntryPrice,
                ExitPrice = t.ExitPrice,
                RealizedPnl = t.Pnl,
                StrategyName = request.Strategy.ToString()
            }).ToList();

            // Build comparison rows grouped by strategy when comparing
            // (single-run result corresponds to request.Strategy)
            // If trades may contain multiple strategies, group them by StrategyName; otherwise this returns a single row
            var comparisonRows = Core.Backtest.StrategySummaryBuilder.BuildComparisonRowsFromTrades(trades, 1000m).ToList();

            // compute summary metrics using builder for consistency
            var summary = Core.Backtest.StrategySummaryBuilder.BuildSummary(request.Strategy.ToString(), trades, 1000m);

            PublishLog($"[回测] 汇总: 净利润={summary.NetPnl}, 成交={summary.TradeCount}, 胜率={summary.WinRate:P1}, 最大回撤={summary.MaxDrawdown}");

            return new BacktestServiceResult(summary, trades);
        }
    }
}
