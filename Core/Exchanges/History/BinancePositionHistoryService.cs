namespace AiFuturesTerminal.Core.Exchanges.History;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Analytics;

/// <summary>
/// Reconstruct position lifecycles from trade history using existing TradeBook logic where possible.
/// For MVP, this implementation will group trades by symbol and position side and build open-close cycles.
/// </summary>
public sealed class BinancePositionHistoryService : AiFuturesTerminal.Core.History.IPositionHistoryService
{
    private readonly AiFuturesTerminal.Core.History.ITradeHistoryService _tradeHistory;

    public BinancePositionHistoryService(AiFuturesTerminal.Core.History.ITradeHistoryService tradeHistory)
    {
        _tradeHistory = tradeHistory ?? throw new ArgumentNullException(nameof(tradeHistory));
    }

    public async Task<IReadOnlyList<PositionHistoryRecord>> QueryPositionsAsync(HistoryQuery query, CancellationToken ct = default)
    {
        // read trades from trade history service and reconstruct positions
        var trades = await _tradeHistory.QueryTradesAsync(query, ct).ConfigureAwait(false);
        if (trades == null || trades.Count == 0) return Array.Empty<PositionHistoryRecord>();

        var positions = new List<PositionHistoryRecord>();

        // group by symbol and position side
        var grouped = trades.GroupBy(t => (t.Symbol, t.PositionSide));

        foreach (var grp in grouped)
        {
            var list = grp.OrderBy(t => t.Time).ToList();
            decimal runningQty = 0m;
            decimal entryPriceAcc = 0m; // for weighted avg
            DateTimeOffset? openTime = null;
            string posSide = grp.Key.PositionSide ?? string.Empty;

            foreach (var tr in list)
            {
                // determine signed qty relative to position side
                var signedQty = tr.Qty * (tr.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? 1m : -1m);

                // if currently flat and trade opens position
                if (runningQty == 0m && signedQty != 0m)
                {
                    // start new lifecycle
                    runningQty = Math.Abs(signedQty);
                    entryPriceAcc = tr.Price * Math.Abs(signedQty);
                    openTime = tr.Time;
                }
                else if (runningQty != 0m && Math.Sign(runningQty) == Math.Sign(signedQty))
                {
                    // increase existing position
                    entryPriceAcc += tr.Price * Math.Abs(signedQty);
                    runningQty += Math.Abs(signedQty);
                }
                else if (runningQty != 0m && Math.Sign(runningQty) != Math.Sign(signedQty))
                {
                    // reduce or close
                    var closeQty = Math.Min(runningQty, Math.Abs(signedQty));
                    // compute realized pnl for the closed portion using simple difference (this may be refined to match tradebook logic)
                    var avgEntry = entryPriceAcc / runningQty;
                    var realized = (tr.Price - avgEntry) * closeQty * (posSide.Equals("LONG", StringComparison.OrdinalIgnoreCase) ? 1m : -1m);

                    var closeTime = tr.Time;
                    var closePrice = tr.Price;
                    var quantityClosed = closeQty;

                    // produce a PositionHistoryRecord for closed cycle
                    var rec = new PositionHistoryRecord
                    {
                        Symbol = grp.Key.Symbol,
                        PositionSide = posSide,
                        EntryPrice = avgEntry,
                        ClosePrice = closePrice,
                        Quantity = quantityClosed,
                        OpenTime = openTime ?? tr.Time,
                        CloseTime = closeTime,
                        RealizedPnl = realized,
                        MaxDrawdown = 0m,
                        StrategyId = tr.StrategyId
                    };

                    positions.Add(rec);

                    // adjust runningQty and entry price accumulator
                    runningQty = Math.Abs(runningQty - closeQty);
                    if (runningQty > 0)
                    {
                        // reduce entryPriceAcc proportionally
                        entryPriceAcc = avgEntry * runningQty;
                        // keep openTime as original
                    }
                    else
                    {
                        // fully closed
                        runningQty = 0m;
                        entryPriceAcc = 0m;
                        openTime = null;
                    }
                }
            }
        }

        // apply filters StrategyId and Symbol if requested
        var filtered = positions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.StrategyId)) filtered = filtered.Where(p => string.Equals(p.StrategyId, query.StrategyId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Symbol)) filtered = filtered.Where(p => string.Equals(p.Symbol, query.Symbol, StringComparison.OrdinalIgnoreCase));

        var skip = (query.Page - 1) * query.PageSize;
        return filtered.Skip(skip).Take(query.PageSize).ToArray();
    }
}