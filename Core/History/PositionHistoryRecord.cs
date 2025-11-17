namespace AiFuturesTerminal.Core.History;

using System;

public sealed record PositionHistoryRecord
{
    public string Symbol { get; init; } = null!;
    public string PositionSide { get; init; } = null!; // LONG / SHORT
    public decimal EntryPrice { get; init; }
    public decimal ClosePrice { get; init; }
    public decimal Quantity { get; init; }
    public DateTimeOffset OpenTime { get; init; }
    public DateTimeOffset CloseTime { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal MaxDrawdown { get; init; }
    public string? StrategyId { get; init; }
}