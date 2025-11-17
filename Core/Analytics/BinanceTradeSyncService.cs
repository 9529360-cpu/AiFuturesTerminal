using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Exchanges.Binance;

namespace AiFuturesTerminal.Core.Analytics
{
    public sealed class BinanceTradeSyncService
    {
        private readonly BinanceAdapter _adapter;

        public BinanceTradeSyncService(BinanceAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        // Get realized pnl entries (income) mapped to TradeRecord within date range
        public async Task<IReadOnlyList<TradeRecord>> GetTradesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            var list = new List<TradeRecord>();

            // Query income entries (REALIZED_PNL) across symbols
            try
            {
                var incomes = await _adapter.GetIncomeAsync(fromUtc, toUtc, ct).ConfigureAwait(false);
                foreach (var inc in incomes)
                {
                    if (!string.Equals(inc.IncomeType, "REALIZED_PNL", StringComparison.OrdinalIgnoreCase)) continue;

                    var record = new TradeRecord
                    {
                        OpenTime = inc.Time,
                        CloseTime = inc.Time,
                        Symbol = inc.Symbol ?? string.Empty,
                        Side = inc.PositionSide.Equals("LONG", StringComparison.OrdinalIgnoreCase) ? TradeSide.Long : TradeSide.Short,
                        Quantity = inc.Quantity,
                        EntryPrice = 0m,
                        ExitPrice = 0m,
                        RealizedPnl = inc.Income,
                        Fee = inc.Commission,
                        StrategyName = "BinanceSync",
                        Mode = inc.Mode
                    };

                    list.Add(record);
                }
            }
            catch
            {
                // swallow
            }

            // Optionally also pull userTrades per symbol if more detailed per-trade records required
            // For simplicity, return income-based realized records which represent settled pnl.

            return list.Where(r => r.CloseTime >= fromUtc && r.CloseTime <= toUtc).ToList();
        }

        public async Task<IReadOnlyList<TradeRecord>> GetTradesForDateAsync(DateOnly date, CancellationToken ct = default)
        {
            var from = date.DateTimeAtStart();
            var to = date.DateTimeAtEnd();
            return await GetTradesAsync(from, to, ct).ConfigureAwait(false);
        }
    }

    static class DateOnlyExtensions
    {
        public static DateTime DateTimeAtStart(this DateOnly d) => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
        public static DateTime DateTimeAtEnd(this DateOnly d) => new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, DateTimeKind.Utc);
    }
}
