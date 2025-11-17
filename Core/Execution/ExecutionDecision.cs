namespace AiFuturesTerminal.Core.Execution;

using System;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// 执行决策的类型枚举
/// </summary>
public enum ExecutionDecisionType
{
    None = 0,
    OpenLong = 1,
    OpenShort = 2,
    Close = 3
}

/// <summary>
/// 表示策略/Agent 发出的执行决策，包含下单信息与元数据
/// </summary>
public sealed record ExecutionDecision
{
    /// <summary>决策类型</summary>
    public ExecutionDecisionType Type { get; init; }

    /// <summary>交易对标识</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>仓位方向/Side</summary>
    public PositionSide Side { get; set; } = PositionSide.Flat;

    /// <summary>下单数量（资产单位或合约张数，视交易品种而定）</summary>
    public decimal? Quantity { get; set; }

    /// <summary>预期的开仓价格（可选）</summary>
    public decimal? EntryPrice { get; set; }

    /// <summary>最近价格（可选，通常用于平仓/估值）</summary>
    public decimal? LastPrice { get; set; }

    /// <summary>止损价（可选）</summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>止盈价（可选）</summary>
    public decimal? TakeProfitPrice { get; set; }

    /// <summary>下单名义金额（USDT，视情况填写）</summary>
    public decimal? Notional { get; set; }

    /// <summary>策略/原因描述（短文本）</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 新增：由上层传入的策略名/标识，用于在 TradeRecord 中保留来源策略
    /// </summary>
    public string? StrategyName { get; init; }

    /// <summary>创建一个 None 决策的工厂方法，可指定策略名以便下层记录</summary>
    public static ExecutionDecision None(string symbol, string reason = "no_action", string? strategyName = null)
        => new() { Type = ExecutionDecisionType.None, Symbol = symbol, Reason = reason, StrategyName = strategyName };
}
