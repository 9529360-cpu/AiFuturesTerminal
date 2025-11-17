namespace AiFuturesTerminal.Core.Execution;

using AiFuturesTerminal.Core.Models;

public static class BinancePnlCalculator
{
    public static decimal CalculateRealizedPnlUsdM(
        PositionSide side,
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity)
    {
        var direction = side == PositionSide.Long ? 1m : -1m;
        return (exitPrice - entryPrice) * quantity * direction;
    }

    public static decimal CalculateUnrealizedPnlUsdM(
        PositionSide side,
        decimal entryPrice,
        decimal markPrice,
        decimal quantity)
    {
        var direction = side == PositionSide.Long ? 1m : -1m;
        return (markPrice - entryPrice) * quantity * direction;
    }
}
