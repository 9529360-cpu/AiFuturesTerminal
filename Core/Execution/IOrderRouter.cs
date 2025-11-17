namespace AiFuturesTerminal.Core.Execution;

using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;

/// <summary>
/// 抽象订单路由器，负责把 ExecutionDecision 转成下单/平仓操作
/// </summary>
public interface IOrderRouter
{
    Task<ExecutionResult> RouteAsync(ExecutionDecision decision, System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// 执行结果占位类型，包含订单状态与成交信息
/// </summary>
public sealed class ExecutionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
}
