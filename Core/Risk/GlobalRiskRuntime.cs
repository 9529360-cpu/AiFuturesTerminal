namespace AiFuturesTerminal.Core.Risk
{
    using System;

    public sealed class GlobalRiskSettings
    {
        public decimal RiskPerTrade { get; init; }          // 单笔风险占比
        public int MaxTradesPerDay { get; init; }           // 单日最大开仓次数
        public int MaxConsecutiveLoss { get; init; }        // 允许连续亏损次数
    }

    public sealed class GlobalRiskRuntime
    {
        public DateOnly TradingDate { get; private set; }
        public int TradesToday { get; private set; }
        public int ConsecutiveLossCount { get; private set; }
        public bool IsFrozen { get; private set; }
        public string? FrozenReason { get; private set; }

        // 手动 Kill Switch 标志
        public bool IsManualFrozen { get; private set; }

        public void ResetFor(DateOnly date)
        {
            TradingDate = date;
            TradesToday = 0;
            ConsecutiveLossCount = 0;
            IsFrozen = false;
            FrozenReason = null;
            IsManualFrozen = false;
        }

        public void OnTradeClosed(decimal pnl)
        {
            TradesToday++;

            if (pnl < 0)
                ConsecutiveLossCount++;
            else if (pnl > 0)
                ConsecutiveLossCount = 0;
            // 持平就不动
        }

        public void Freeze(string reason)
        {
            IsFrozen = true;
            FrozenReason = reason;
        }

        public void FreezeManually(string? reason = null)
        {
            IsManualFrozen = true;
            IsFrozen = true;
            FrozenReason = reason ?? "手动 Kill Switch：暂停新开仓";
        }

        public void UnfreezeManually()
        {
            IsManualFrozen = false;
            // 简化处理：解除人工冻结同时清理 IsFrozen 与 FrozenReason
            IsFrozen = false;
            FrozenReason = null;
        }
    }
}
