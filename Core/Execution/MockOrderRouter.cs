namespace AiFuturesTerminal.Core.Execution;

using System;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges.Mock;
using AiFuturesTerminal.Core.Models;

public sealed class MockOrderRouter : IOrderRouter
{
    private readonly MockExchangeAdapter _adapter;

    public MockOrderRouter(MockExchangeAdapter adapter)
    {
        _adapter = adapter;
    }

    public async Task<ExecutionResult> RouteAsync(ExecutionDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision == null) return new ExecutionResult { Success = false, Message = "null_decision" };

        try
        {
            // simple mapping
            switch (decision.Type)
            {
                case ExecutionDecisionType.OpenLong:
                    await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Long, decision.Quantity ?? 1m, decision.Reason, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 开多", Symbol = decision.Symbol };
                case ExecutionDecisionType.OpenShort:
                    await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Short, decision.Quantity ?? 1m, decision.Reason, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 开空", Symbol = decision.Symbol };
                case ExecutionDecisionType.Close:
                    await _adapter.ClosePositionAsync(decision.Symbol, decision.Reason, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 平仓", Symbol = decision.Symbol };
                default:
                    return new ExecutionResult { Success = false, Message = "none", Symbol = decision.Symbol };
            }
        }
        catch (InvalidOperationException iex)
        {
            // Known expected error from adapter (e.g., already have position) -> treat as non-fatal rejection
            var msg = iex.Message ?? "adapter_invalid_operation";
            try { Console.WriteLine($"[模拟路由器] 适配器拒绝委托: {msg}"); } catch { }
            return new ExecutionResult { Success = false, Message = msg, Symbol = decision.Symbol };
        }
        catch (Exception ex)
        {
            // unexpected errors: return failed execution result but do not throw to keep backtest running
            try { Console.WriteLine($"[模拟路由器] 路由委托时发生意外错误: {ex.Message}"); } catch { }
            return new ExecutionResult { Success = false, Message = ex.Message, Symbol = decision.Symbol };
        }
    }
}
