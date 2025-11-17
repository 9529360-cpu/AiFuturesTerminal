namespace AiFuturesTerminal.Core.Risk
{
    public sealed class GlobalRiskGuard : IGlobalRiskGuard
    {
        public GlobalRiskDecision CanOpenNewPosition(GlobalRiskSettings settings, GlobalRiskRuntime runtime)
        {
            if (runtime.IsFrozen)
                return new GlobalRiskDecision(false, runtime.FrozenReason ?? "风控已熔断");

            if (settings.MaxTradesPerDay > 0 && runtime.TradesToday >= settings.MaxTradesPerDay)
            {
                return new GlobalRiskDecision(false, $"已达单日最大开仓次数 {settings.MaxTradesPerDay} 笔");
            }

            if (settings.MaxConsecutiveLoss > 0 && runtime.ConsecutiveLossCount >= settings.MaxConsecutiveLoss)
            {
                return new GlobalRiskDecision(false, $"已连续亏损 {runtime.ConsecutiveLossCount} 笔，触发风控冷却");
            }

            return new GlobalRiskDecision(true, null);
        }
    }
}
