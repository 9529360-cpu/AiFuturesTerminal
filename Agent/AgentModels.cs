namespace AiFuturesTerminal.Agent;

using System;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Strategy;

/// <summary>
/// 智能体的决策结果，包含推荐执行决策、解释文本、策略名与信心度。
/// </summary>
public sealed record AgentDecision
{
    /// <summary>智能体推荐的执行决策。</summary>
    public ExecutionDecision ExecutionDecision { get; init; } = ExecutionDecision.None(string.Empty);

    /// <summary>面向用户的解释文本。</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>用于标识采用的策略名。</summary>
    public string StrategyName { get; init; } = string.Empty;

    /// <summary>信心度，0-1。</summary>
    public decimal Confidence { get; init; }
}

/// <summary>
/// 智能体决策所需输入上下文：交易对、历史、当前持仓、账户与当前时间。
/// </summary>
public sealed class AgentContext
{
    public string Symbol { get; init; } = string.Empty;
    public IReadOnlyList<Candle> History { get; init; } = Array.Empty<Candle>();
    public AiFuturesTerminal.Core.Models.Position? CurrentPosition { get; init; }
    public AiFuturesTerminal.Core.Models.AccountSnapshot Account { get; init; } = new(0m, 0m, DateTime.UtcNow);
    public DateTime Now { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 可选的策略类型覆盖。若为 null，则使用全局 StrategyConfig.Kind。
    /// 将来多策略调度时，可以在这里按 symbol 指定不同策略。
    /// </summary>
    public StrategyKind? StrategyOverride { get; init; }
}
