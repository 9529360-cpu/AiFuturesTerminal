using System;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Analytics;

public sealed class TradeRecord
{
    public DateTime OpenTime { get; init; }
    public DateTime CloseTime { get; init; }

    public string Symbol { get; init; } = string.Empty;

    public TradeSide Side { get; init; }

    public decimal Quantity { get; init; }        // 资产数量/合约张数
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }

    public decimal RealizedPnl { get; init; }     // 以 USDT 表示的盈亏
    public decimal Fee { get; init; }              // 手续费（USDT，负数代表扣费）
    public string StrategyName { get; init; } = string.Empty;
    public string? Notes { get; init; }           // 额外注释/标签

    // 执行模式：DryRun / Testnet
    public ExecutionMode Mode { get; init; } = ExecutionMode.DryRun;

    // --- Exchange identifiers (optional) for reconciliation ---
    public string? ExchangeOrderId { get; init; }
    public string? ExchangeTradeId { get; init; }
}
