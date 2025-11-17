namespace AiFuturesTerminal.Core.History;

using System;

public sealed record HistoryQuery
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string? Symbol { get; init; }
    public string? StrategyId { get; init; }
    public string? Side { get; init; }
    public string? RunId { get; init; }
    public int Page { get; init; } = 1;
    private int _pageSize = 1000;
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, 2000);
    }
}