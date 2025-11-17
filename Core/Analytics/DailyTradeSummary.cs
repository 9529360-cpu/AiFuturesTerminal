using System;

namespace AiFuturesTerminal.Core.Analytics;

public sealed class DailyTradeSummary
{
    public DateOnly TradingDate { get; init; }

    public int TradeCount { get; init; }
    public int WinCount { get; init; }
    public int LoseCount { get; init; }

    public decimal TotalPnL { get; init; }
    public decimal MaxDrawdown { get; init; }

    public decimal WinRate => TradeCount == 0 ? 0m : (decimal)WinCount / TradeCount;
}
