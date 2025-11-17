namespace AiFuturesTerminal.Core.Backtest;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.History;
using Microsoft.Extensions.Logging;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core;

public sealed class BacktestPersistHookRegistrar
{
    public BacktestPersistHookRegistrar(AppEnvironmentOptions env, IBacktestHistoryService persister, ILogger<BacktestPersistHookRegistrar>? logger = null)
    {
        // honor configuration: if disabled, do not register persistence hook
        if (env == null) throw new ArgumentNullException(nameof(env));
        if (!env.EnableBacktestHistoryPersist)
        {
            try { logger?.LogInformation("Backtest persistence disabled by configuration (EnableBacktestHistoryPersist=false). Hook not registered."); } catch { }
            return;
        }

        // register hook
        BacktestEngine.BacktestResultPersistHook = async (result) =>
        {
            try
            {
                // generate run id
                var strategyId = !string.IsNullOrWhiteSpace(result.StrategyId) ? result.StrategyId : result.Symbol;
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var shortGuid = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
                var runId = $"{strategyId}-{ts}-{shortGuid}";

                var th = new List<AiFuturesTerminal.Core.History.TradeHistoryRecord>();
                foreach (var t in result.Trades)
                {
                    th.Add(new AiFuturesTerminal.Core.History.TradeHistoryRecord
                    {
                        TradeId = 0,
                        OrderId = 0,
                        Symbol = t.Symbol,
                        Side = t.Side == Core.Models.PositionSide.Long ? "BUY" : "SELL",
                        PositionSide = t.Side == Core.Models.PositionSide.Long ? "LONG" : "SHORT",
                        Price = t.ExitPrice,
                        Qty = t.Quantity,
                        QuoteQty = t.ExitPrice * t.Quantity,
                        RealizedPnl = t.Pnl,
                        Commission = 0m,
                        CommissionAsset = string.Empty,
                        Time = new DateTimeOffset(t.ExitTime),
                        StrategyId = result.StrategyId,
                        RunId = runId
                    });
                }

                await persister.PersistBacktestAsync(runId, th, Array.Empty<AiFuturesTerminal.Core.History.OrderHistoryRecord>()).ConfigureAwait(false);
                logger?.LogInformation($"BacktestPersistHook: persisted run {runId} trades={th.Count}");
            }
            catch (Exception ex)
            {
                try { logger?.LogError(ex, "BacktestPersistHook error"); } catch { }
            }
        };
    }
}
