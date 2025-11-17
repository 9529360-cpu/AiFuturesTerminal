namespace AiFuturesTerminal.Core.History;

using System;

public sealed record OrderHistoryRecord
{
    public long ExchangeOrderId { get; init; }
    public string Symbol { get; init; } = null!;
    public string Side { get; init; } = null!; // BUY / SELL
    public string PositionSide { get; init; } = null!; // LONG / SHORT / BOTH
    public string Type { get; init; } = null!; // LIMIT / MARKET ...
    public string Status { get; init; } = null!; // NEW / FILLED / CANCELED ...
    public decimal Price { get; init; }
    public decimal OrigQty { get; init; }
    public decimal ExecutedQty { get; init; }
    public decimal AvgPrice { get; init; }
    public DateTimeOffset CreateTime { get; init; }
    public DateTimeOffset UpdateTime { get; init; }

    // strategy related
    public string? StrategyId { get; init; }
    public string? ClientOrderId { get; init; }
    public string? RunId { get; init; }
}