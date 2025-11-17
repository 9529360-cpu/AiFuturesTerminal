namespace AiFuturesTerminal.Core.Execution;

using System;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Environment;

/// <summary>
/// 基础风控引擎：支持单仓位逻辑并对开仓策略进行简单校验（每单最大风险占比）。
/// TODO: 未来可加入价格、止损距离等信息以计算实际下单数量。
/// </summary>
public sealed class BasicRiskEngine : IRiskEngine
{
    private readonly decimal _maxRiskPerTradePercent;

    /// <summary>
    /// 构造 BasicRiskEngine。
    /// </summary>
    /// <param name="maxRiskPerTradePercent">单笔最大风险占比（例如 0.01 表示 1%）。</param>
    public BasicRiskEngine(decimal maxRiskPerTradePercent)
    {
        if (maxRiskPerTradePercent < 0m || maxRiskPerTradePercent > 0.05m)
            throw new ArgumentOutOfRangeException(nameof(maxRiskPerTradePercent), "maxRiskPerTradePercent must be between 0 and 0.05");

        _maxRiskPerTradePercent = maxRiskPerTradePercent;
    }

    /// <summary>
    /// 应用风控规则：当前仅实现单仓约束和在理由中标注已通过风控检查。
    /// 如果已有持仓，则拒绝开新仓。
    /// </summary>
    public ExecutionDecision ApplyRiskRules(AccountSnapshot account, Position? currentPosition, ExecutionDecision rawDecision)
    {
        if (rawDecision.Type == ExecutionDecisionType.None) return rawDecision;

        // 如果已有持仓且非空仓，禁止开新仓
        if (currentPosition != null && !currentPosition.IsFlat())
        {
            if (rawDecision.Type == ExecutionDecisionType.OpenLong || rawDecision.Type == ExecutionDecisionType.OpenShort)
            {
                return rawDecision with { Type = ExecutionDecisionType.None, Reason = rawDecision.Reason + ";already_have_position" };
            }

            // 平仓放行
            return rawDecision;
        }

        // 没有持仓，允许开仓，但不在此处计算具体数量（缺少价格信息）
        if (rawDecision.Type == ExecutionDecisionType.OpenLong || rawDecision.Type == ExecutionDecisionType.OpenShort)
        {
            // 确保价格可用
            var price = rawDecision.EntryPrice ?? rawDecision.LastPrice ?? 0m;
            if (price <= 0m)
            {
                // 记录详细日志以帮助查找缺失价格的位置
                var symbol = rawDecision.Symbol ?? "";
                var reason = rawDecision.Reason ?? "";
                var msg = $"[Risk] invalid pricing for {symbol}, reason={reason}, entry={rawDecision.EntryPrice}, last={rawDecision.LastPrice}";
                try { Console.WriteLine(msg); } catch { }

                // 显式拒绝该决策
                return rawDecision with { Type = ExecutionDecisionType.None, Reason = rawDecision.Reason + ";invalid_price" };
            }

            // 执行简单的风险标记（保持原有行为：标记原因）
            return rawDecision with { Reason = rawDecision.Reason + ";risk_checked" };
        }

        return rawDecision;
    }

    /// <summary>
    /// Check whether exchange has an open position for symbol and reject open decisions if so.
    /// This method is safe to call for Testnet/Live environments to ensure exchange is authoritative.
    /// </summary>
    public async System.Threading.Tasks.Task<ExecutionDecision> RejectIfExchangeHasPositionAsync(ITradingEnvironment env, ExecutionDecision decision)
    {
        if (env == null) return decision;
        if (decision.Type != ExecutionDecisionType.OpenLong && decision.Type != ExecutionDecisionType.OpenShort) return decision;

        try
        {
            var pos = await env.GetOpenPositionAsync(decision.Symbol ?? string.Empty).ConfigureAwait(false);
            if (pos != null && !pos.IsFlat())
            {
                var msg = $"风控：{decision.Symbol} 在币安上仍有未平仓位，禁止重复开仓";
                // use engine-level event? For now, write to console
                try { Console.WriteLine($"[Risk] {msg}"); } catch { }
                return decision with { Type = ExecutionDecisionType.None, Reason = (decision.Reason ?? string.Empty) + ";exchange_has_open_position" };
            }
        }
        catch { }

        return decision;
    }
}
