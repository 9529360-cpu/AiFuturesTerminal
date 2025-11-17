namespace AiFuturesTerminal.Core.Analytics;

using System;
using System.Collections.Generic;
using System.Linq;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Execution;

public static class TradeHistoryToTradeRecordConverter
{
    // Convert a sequence of fill-level TradeHistoryRecord into position-level TradeRecord entries
    // Algorithm: group by (Symbol, PositionSide) and rebuild open/close lifecycles using FIFO matching
    public static IEnumerable<TradeRecord> Convert(IEnumerable<TradeHistoryRecord> fills, string? runId = null)
    {
        if (fills == null) yield break;

        // group by symbol + position side
        var grouped = fills.OrderBy(t => t.Time).GroupBy(t => (t.Symbol, PositionSide: t.PositionSide ?? string.Empty));

        foreach (var grp in grouped)
        {
            var list = grp.ToList();
            decimal runningQty = 0m; // always positive magnitude
            decimal entryPriceAcc = 0m; // weighted price accumulator
            DateTimeOffset? openTime = null;
            string posSide = grp.Key.PositionSide ?? string.Empty;

            foreach (var tr in list)
            {
                // signed qty based on trade side (BUY = positive for LONG, SELL = negative for LONG)
                var signedQty = tr.Qty * (tr.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? 1m : -1m);

                // Interpret position side: if posSide indicates SHORT, invert sign interpretation
                // For futures userTrades sometimes positionSide indicates LONG/SHORT; we keep signedQty as trade direction

                if (runningQty == 0m && signedQty != 0m)
                {
                    // open new lifecycle
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
                    // reduce or close existing position
                    var closeQty = Math.Min(runningQty, Math.Abs(signedQty));

                    var avgEntry = entryPriceAcc / runningQty;
                    var realized = (tr.Price - avgEntry) * closeQty * (posSide.Equals("LONG", StringComparison.OrdinalIgnoreCase) ? 1m : -1m);

                    var closeTime = tr.Time;
                    var closePrice = tr.Price;
                    var quantityClosed = closeQty;

                    // map position side to TradeSide enum
                    var tradeSide = posSide.Equals("LONG", StringComparison.OrdinalIgnoreCase) ? TradeSide.Long : TradeSide.Short;

                    var record = new TradeRecord
                    {
                        OpenTime = (openTime ?? tr.Time).UtcDateTime,
                        CloseTime = closeTime.UtcDateTime,
                        Symbol = grp.Key.Symbol,
                        Side = tradeSide,
                        Quantity = quantityClosed,
                        EntryPrice = avgEntry,
                        ExitPrice = closePrice,
                        RealizedPnl = realized,
                        Fee = tr.Commission * (quantityClosed / Math.Abs(signedQty)), // allocate proportional fee from closing trade
                        StrategyName = tr.StrategyId ?? string.Empty,
                        Mode = ExecutionMode.DryRun,
                        ExchangeOrderId = tr.OrderId == 0 ? null : tr.OrderId.ToString(),
                        ExchangeTradeId = tr.TradeId == 0 ? null : tr.TradeId.ToString(),
                        Notes = runId
                    };

                    yield return record;

                    // adjust runningQty and entryPriceAcc
                    runningQty = Math.Abs(runningQty - closeQty);
                    if (runningQty > 0)
                    {
                        entryPriceAcc = avgEntry * runningQty;
                        // keep openTime as original
                    }
                    else
                    {
                        runningQty = 0m;
                        entryPriceAcc = 0m;
                        openTime = null;
                    }
                }
            }
        }
    }
}
