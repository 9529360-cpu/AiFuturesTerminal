using System;
using System.Linq;
using AiFuturesTerminal.Core.Analytics;
using System.Collections.Generic;
using System.Threading;

namespace AiFuturesTerminal.Core.Risk
{
    public sealed class GlobalRiskStatus
    {
        public decimal TodayRealizedPnl { get; init; }
        public decimal TodayMaxDrawdown { get; init; }
        public int ConsecutiveLosingTrades { get; init; }
        public bool IsOpenNewPositionsFrozen { get; init; }
    }

    public interface IRiskStatusService
    {
        GlobalRiskStatus GetCurrentStatus();
    }

    public sealed class RiskStatusService : IRiskStatusService, IDisposable
    {
        private readonly ITradeBookForRisk _tradeBookForRisk;
        private readonly AppEnvironmentOptions _envOptions;
        private readonly object _lock = new object();

        // rolling today's trades cache
        private readonly List<decimal> _todayPnls = new List<decimal>();
        private decimal _todayMaxDrawdown = 0m;
        private int _consecutiveLosing = 0;
        private bool _frozen = false;

        private decimal DailyLossLimit => _envOptions?.DailyLossLimit ?? -100m;

        public RiskStatusService(ITradeBookForRisk tradeBookForRisk, AppEnvironmentOptions envOptions)
        {
            _tradeBookForRisk = tradeBookForRisk ?? throw new ArgumentNullException(nameof(tradeBookForRisk));
            _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
            _tradeBookForRisk.TradeClosed += OnTradeClosed;

            // initial aggregation from tradebook for today
            RecomputeFromTradeBook();
        }

        private void OnTradeClosed(object? s, TradeClosedEventArgs e)
        {
            lock (_lock)
            {
                try
                {
                    // update today's pnl list
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    if (e.Time.Date == today.ToDateTime(TimeOnly.MinValue).Date)
                    {
                        _todayPnls.Add(e.Pnl);

                        // update consecutive loses
                        if (e.Pnl < 0)
                        {
                            _consecutiveLosing++;
                        }
                        else
                        {
                            _consecutiveLosing = 0;
                        }

                        // update max drawdown simple computation: running worst cumulative delta
                        var cum = 0m;
                        var peak = 0m;
                        var trough = 0m;
                        foreach (var p in _todayPnls)
                        {
                            cum += p;
                            if (cum > peak) peak = cum;
                            if (cum < trough) trough = cum;
                        }

                        var drawdown = peak - trough;
                        if (drawdown > _todayMaxDrawdown) _todayMaxDrawdown = drawdown;

                        // freeze opens if cumulative loss today <= DailyLossLimit (DailyLossLimit expected negative)
                        var cumulative = _todayPnls.Sum();
                        if (cumulative <= DailyLossLimit) _frozen = true;
                    }
                }
                catch
                {
                    // swallow
                }
            }
        }

        private void RecomputeFromTradeBook()
        {
            lock (_lock)
            {
                try
                {
                    _todayPnls.Clear();
                    _todayMaxDrawdown = 0m;
                    _consecutiveLosing = 0;
                    _frozen = false;

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    // If underlying tradebook supports efficient query, we'd call it; fallback to GetDailySummary
                    if (_tradeBookForRisk is Core.Analytics.ITradeBook tb)
                    {
                        // no direct access to individual trades; best-effort use summary
                        var summary = tb.GetDailySummary(today);
                        // we cannot reconstruct consecutive loses from summary; keep conservative 0
                        _todayPnls.Add(summary.TotalPnL);
                        _todayMaxDrawdown = summary.MaxDrawdown;
                        _consecutiveLosing = 0;
                        _frozen = summary.TotalPnL <= DailyLossLimit;
                    }
                }
                catch
                {
                    // swallow
                }
            }
        }

        public GlobalRiskStatus GetCurrentStatus()
        {
            lock (_lock)
            {
                return new GlobalRiskStatus
                {
                    TodayRealizedPnl = _todayPnls.Sum(),
                    TodayMaxDrawdown = _todayMaxDrawdown,
                    ConsecutiveLosingTrades = _consecutiveLosing,
                    IsOpenNewPositionsFrozen = _frozen
                };
            }
        }

        public void Dispose()
        {
            try { _tradeBookForRisk.TradeClosed -= OnTradeClosed; } catch { }
        }
    }
}
