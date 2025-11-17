using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Backtest;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Analytics
{
    public sealed class StrategySummary
    {
        public string StrategyName { get; init; } = string.Empty;
        public decimal NetPnl { get; init; }
        public decimal GrossProfit { get; init; }
        public decimal GrossLoss { get; init; }
        public decimal MaxDrawdown { get; init; }
        public int TradesCount { get; init; }
        public double WinRate { get; init; }
        public decimal ProfitFactor { get; init; }
    }

    public sealed class DailyTradeSummaryRow
    {
        public DateOnly Date { get; init; }
        public decimal NetPnl { get; init; }
        public decimal GrossProfit { get; init; }
        public decimal GrossLoss { get; init; }
        public decimal MaxDrawdown { get; init; }
        public int TradesCount { get; init; }
        public double WinRate { get; init; }
    }

    public sealed class TradeAnalyticsService
    {
        private ITradeBook _tradeBook;
        private readonly AppEnvironmentOptions _envOptions;
        private readonly BinanceTradeViewService? _binanceView;

        public event EventHandler? TradeBookChanged;

        public TradeAnalyticsService(ITradeBook tradeBook, AppEnvironmentOptions envOptions, BinanceTradeViewService? binanceView = null)
        {
            _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
            _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
            _binanceView = binanceView; // may be null for backtest-only setups
        }

        public ITradeBook CurrentTradeBook => _tradeBook;

        public void SetTradeBook(ITradeBook tb)
        {
            if (tb == null) throw new ArgumentNullException(nameof(tb));
            _tradeBook = tb;
            try { TradeBookChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        public async Task<IReadOnlyList<DailyTradeSummaryRow>> GetDailySummaryAsync(DateTime from, DateTime to, string? strategyName = null, CancellationToken ct = default)
        {
            IReadOnlyList<TradeRecord> trades;

            // For Testnet/Live, prefer BinanceTradeViewService (via IBinanceState) if available
            if ((_envOptions.ExecutionMode == ExecutionMode.Testnet || _envOptions.ExecutionMode == ExecutionMode.Live) && _binanceView != null)
            {
                var list = await _binanceView.GetTodayTradeRecordsAsync(null, ct).ConfigureAwait(false);
                // filter by date range and strategyName (strategyName not present on BinanceTradeViewService records, so ignore strategyName)
                trades = list.Where(t => t.CloseTime >= from.DateTimeAtStart() && t.CloseTime <= to.DateTimeAtEnd()).ToList();
            }
            else
            {
                trades = await _tradeBook.GetTradesAsync(from.DateTimeAtStart(), to.DateTimeAtEnd(), strategyName, ct).ConfigureAwait(false);
            }

            var grouped = trades.GroupBy(t => DateOnly.FromDateTime(t.CloseTime)).OrderBy(g => g.Key);
            var rows = new List<DailyTradeSummaryRow>();

            foreach (var g in grouped)
            {
                var list = g.OrderBy(t => t.CloseTime).ToList();
                var net = list.Sum(t => t.RealizedPnl);
                var gp = list.Where(t => t.RealizedPnl > 0).Sum(t => t.RealizedPnl);
                var gl = list.Where(t => t.RealizedPnl < 0).Sum(t => t.RealizedPnl);
                var tradesCount = list.Count;
                var wins = list.Count(t => t.RealizedPnl > 0);
                var winRate = tradesCount > 0 ? (double)wins / tradesCount : 0.0;

                // simple drawdown calc within day
                decimal equity = 0m;
                decimal peak = 0m;
                decimal maxDd = 0m;
                foreach (var t in list)
                {
                    equity += t.RealizedPnl;
                    if (equity > peak) peak = equity;
                    var dd = peak - equity;
                    if (dd > maxDd) maxDd = dd;
                }

                rows.Add(new DailyTradeSummaryRow
                {
                    Date = g.Key,
                    NetPnl = net,
                    GrossProfit = gp,
                    GrossLoss = gl,
                    MaxDrawdown = maxDd,
                    TradesCount = tradesCount,
                    WinRate = winRate
                });
            }

            return rows;
        }

        public async Task<IReadOnlyList<StrategySummary>> GetStrategySummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            IReadOnlyList<TradeRecord> trades;
            if ((_envOptions.ExecutionMode == ExecutionMode.Testnet || _envOptions.ExecutionMode == ExecutionMode.Live) && _binanceView != null)
            {
                // BinanceTradeViewService provides daily summaries; use its aggregated daily rows to build strategy-level summary is not available.
                // Fallback: fetch recent trades and group by StrategyName (which may be populated only if synced).
                var list = await _binanceView.GetTodayTradeRecordsAsync(null, ct).ConfigureAwait(false);
                trades = list.Where(t => t.CloseTime >= from.DateTimeAtStart() && t.CloseTime <= to.DateTimeAtEnd()).ToList();
            }
            else
            {
                trades = await _tradeBook.GetTradesAsync(from.DateTimeAtStart(), to.DateTimeAtEnd(), null, ct).ConfigureAwait(false);
            }
            var rows = new List<StrategySummary>();

            var groups = trades.GroupBy(t => t.StrategyName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (string.IsNullOrWhiteSpace(g.Key)) continue;
                var list = g.OrderBy(t => t.CloseTime).ToList();
                var net = list.Sum(t => t.RealizedPnl);
                var gp = list.Where(t => t.RealizedPnl > 0).Sum(t => t.RealizedPnl);
                var gl = list.Where(t => t.RealizedPnl < 0).Sum(t => t.RealizedPnl);
                var tradesCount = list.Count;
                var wins = list.Count(t => t.RealizedPnl > 0);
                var winRate = tradesCount > 0 ? (double)wins / tradesCount : 0.0;

                // compute max drawdown across sequence of realized pnl
                decimal equity = 0m;
                decimal peak = 0m;
                decimal maxDd = 0m;
                foreach (var t in list)
                {
                    equity += t.RealizedPnl;
                    if (equity > peak) peak = equity;
                    var dd = peak - equity;
                    if (dd > maxDd) maxDd = dd;
                }

                decimal profitFactor;
                var absGrossLoss = Math.Abs(gl);
                if (absGrossLoss == 0m)
                    profitFactor = gp > 0m ? decimal.MaxValue : 0m;
                else
                    profitFactor = gp / absGrossLoss;

                rows.Add(new StrategySummary
                {
                    StrategyName = g.Key,
                    NetPnl = net,
                    GrossProfit = gp,
                    GrossLoss = gl,
                    MaxDrawdown = maxDd,
                    TradesCount = tradesCount,
                    WinRate = winRate,
                    ProfitFactor = profitFactor
                });
            }

            return rows;
        }
    }

    static class DateTimeExtensions
    {
        public static DateTime DateTimeAtStart(this DateTime dt) => new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);
        public static DateTime DateTimeAtEnd(this DateTime dt) => new DateTime(dt.Year, dt.Month, dt.Day, 23, 59, 59, DateTimeKind.Utc);
    }
}
