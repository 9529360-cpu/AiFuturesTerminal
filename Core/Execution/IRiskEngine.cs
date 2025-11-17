namespace AiFuturesTerminal.Core.Execution;

using AiFuturesTerminal.Core.Models;

/// <summary>
/// 风控引擎接口：根据账户状态、当前仓位与原始决策，返回调整后的决策。
/// </summary>
public interface IRiskEngine
{
    /// <summary>
    /// 应用风控规则，对原始决策进行校正或拒绝。
    /// </summary>
    ExecutionDecision ApplyRiskRules(AccountSnapshot account, Position? currentPosition, ExecutionDecision rawDecision);
}
