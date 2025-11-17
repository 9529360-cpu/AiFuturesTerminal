namespace AiFuturesTerminal.Core.History;

using System;

public sealed record TradeHistoryRecord
{
    public long TradeId { get; init; }
    public long OrderId { get; init; }
    public string Symbol { get; init; } = null!;
    public string Side { get; init; } = null!;
    public string PositionSide { get; init; } = null!;
    public decimal Price { get; init; }
    public decimal Qty { get; init; }
    public decimal QuoteQty { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal Commission { get; init; }
    public string CommissionAsset { get; init; } = null!;
    public DateTimeOffset Time { get; init; }

    public string? StrategyId { get; init; }
    public string? RunId { get; init; }
}