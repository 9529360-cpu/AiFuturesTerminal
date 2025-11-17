namespace AiFuturesTerminal.Core.Analytics;

using System.Collections.Generic;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.History;

public static class HistoricalTradeBookFactory
{
    public static ITradeBook CreateFromHistory(IEnumerable<TradeHistoryRecord> trades, string? runId = null)
    {
        var tb = new InMemoryTradeBook();
        var records = TradeHistoryToTradeRecordConverter.Convert(trades, runId);
        foreach (var r in records)
        {
            tb.AddTrade(r);
        }
        return tb;
    }
}
