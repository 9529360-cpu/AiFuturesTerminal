namespace AiFuturesTerminal.Core.Risk
{
    public sealed record GlobalRiskDecision(bool IsAllowed, string? Reason = null);

    public interface IGlobalRiskGuard
    {
        GlobalRiskDecision CanOpenNewPosition(GlobalRiskSettings settings, GlobalRiskRuntime runtime);
    }
}
