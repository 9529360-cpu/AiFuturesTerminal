using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Analytics;

namespace AiFuturesTerminal.Core.Analytics
{
    public sealed class BinanceTradeViewService : IBinanceTradeViewService
    {
        private readonly IBinanceState _state;

        public BinanceTradeViewService(IBinanceState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Return raw core TradeRecord list (used by analytics/backend code).
        /// </summary>
        public async Task<IReadOnlyList<TradeRecord>> GetTodayTradeRecordsAsync(string? symbol, CancellationToken ct = default)
        {
            var today = DateTime.UtcNow.Date;
            var list = await _state.GetRecentTradesAsync(today, today.AddDays(1).AddSeconds(-1), symbol, ct).ConfigureAwait(false);
            return list.OrderBy(t => t.CloseTime).ToList();
        }

        /// <summary>
        /// UI-facing trade DTO list for today's trades.
        /// </summary>
        public async Task<IReadOnlyList<UiTodayTradeRow>> GetTodayTradesAsync(string? symbol = null, CancellationToken cancellationToken = default)
        {
            var core = await GetTodayTradeRecordsAsync(symbol, cancellationToken).ConfigureAwait(false);
            var mapped = core.Select(t => new UiTodayTradeRow
            {
                Time = t.CloseTime,
                Symbol = t.Symbol ?? string.Empty,
                Side = t.Side.ToString(),
                Quantity = t.Quantity,
                Notional = t.Quantity * t.ExitPrice,
                EntryPrice = t.EntryPrice,
                ExitPrice = t.ExitPrice,
                RealizedPnl = t.RealizedPnl,
                Fee = t.Fee
            }).ToList();

            return mapped;
        }

        /// <summary>
        /// Simple aggregation for today's summary based on trades (uses GetTodayTradeRecordsAsync)
        /// </summary>
        public async Task<TodaySummaryDto> GetTodaySummaryAsync(string? symbol = null, CancellationToken cancellationToken = default)
        {
            var trades = await GetTodayTradeRecordsAsync(symbol, cancellationToken).ConfigureAwait(false);
            var tradeCount = trades.Count;
            var winCount = trades.Count(t => t.RealizedPnl > 0);
            var grossProfit = trades.Where(t => t.RealizedPnl > 0).Sum(t => t.RealizedPnl);
            var grossLoss = trades.Where(t => t.RealizedPnl < 0).Sum(t => t.RealizedPnl);
            var netPnl = trades.Sum(t => t.RealizedPnl - t.Fee);
            double winRate = tradeCount == 0 ? 0.0 : (double)winCount / tradeCount;

            // compute MaxDrawdown based on cumulative realized pnl over the day
            decimal equity = 0m;
            decimal peak = 0m;
            decimal maxDd = 0m;
            var ordered = trades.OrderBy(t => t.CloseTime).ToList();
            foreach (var tr in ordered)
            {
                equity += tr.RealizedPnl;
                if (equity > peak) peak = equity;
                var dd = peak - equity;
                if (dd > maxDd) maxDd = dd;
            }

            return new TodaySummaryDto
            {
                NetPnl = netPnl,
                GrossProfit = grossProfit,
                GrossLoss = grossLoss,
                Trades = tradeCount,
                WinRate = winRate,
                MaxDrawdown = maxDd
            };
        }

        public async Task<IReadOnlyList<DailyTradeSummary>> GetDailySummaryAsync(DateTime from, DateTime to, string? symbol, CancellationToken ct = default)
        {
            var rows = await _state.GetDailyPnlAsync(from, to, symbol, ct).ConfigureAwait(false);
            var groups = rows.GroupBy(r => r.Date.Date).OrderBy(g => g.Key);
            var outRows = new List<DailyTradeSummary>();
            foreach (var g in groups)
            {
                var date = DateOnly.FromDateTime(g.Key);
                var total = g.Sum(x => x.RealizedPnl);
                var trades = g.Count();
                var win = g.Count(x => x.RealizedPnl > 0);
                var lose = g.Count(x => x.RealizedPnl < 0);
                var maxDd = 0m; // placeholder

                outRows.Add(new DailyTradeSummary { TradingDate = date, TradeCount = trades, WinCount = win, LoseCount = lose, TotalPnL = total, MaxDrawdown = maxDd });
            }

            return outRows;
        }
    }

    // UI-facing DTOs
    public sealed class UiTodayTradeRow
    {
        public DateTime Time { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // Long/Short
        public decimal Quantity { get; set; }
        public decimal Notional { get; set; } // USDT
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal Fee { get; set; }
    }

    public sealed class TodaySummaryDto
    {
        public decimal NetPnl { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal GrossLoss { get; set; }
        public int Trades { get; set; }
        public double WinRate { get; set; }
        public decimal MaxDrawdown { get; set; }
    }
}
