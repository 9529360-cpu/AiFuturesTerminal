namespace AiFuturesTerminal.Core.Strategy;

using AiFuturesTerminal.Core.Execution;

/// <summary>
/// 统一策略接口，供回测与实盘共用
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 在每个 K 线（bar）到来时被调用，返回执行决策
    /// </summary>
    /// <param name="context">运行时上下文</param>
    /// <returns>执行决策（可以为 None）</returns>
    ExecutionDecision OnBar(StrategyContext context);
}
