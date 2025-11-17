namespace AiFuturesTerminal.Core.Execution;

using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges.Binance;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// BinanceOrderRouter routes execution decisions to the BinanceAdapter.
/// It should not record trades itself; the adapter's response or websocket/order updates will be used to record fills.
/// </summary>
public sealed class BinanceOrderRouter : IOrderRouter
{
    private readonly BinanceAdapter _adapter;

    public BinanceOrderRouter(BinanceAdapter adapter)
    {
        _adapter = adapter;
    }

    public async Task<ExecutionResult> RouteAsync(ExecutionDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision == null) return new ExecutionResult { Success = false, Message = "null_decision" };

        try
        {
            switch (decision.Type)
            {
                case ExecutionDecisionType.OpenLong:
                    await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Long, decision.Quantity ?? 0m, decision.Reason ?? string.Empty, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 开多", Symbol = decision.Symbol };
                case ExecutionDecisionType.OpenShort:
                    await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Short, decision.Quantity ?? 0m, decision.Reason ?? string.Empty, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 开空", Symbol = decision.Symbol };
                case ExecutionDecisionType.Close:
                    await _adapter.ClosePositionAsync(decision.Symbol, decision.Reason ?? string.Empty, cancellationToken).ConfigureAwait(false);
                    return new ExecutionResult { Success = true, Message = "已下单 平仓", Symbol = decision.Symbol };
                default:
                    return new ExecutionResult { Success = false, Message = "none", Symbol = decision.Symbol };
            }
        }
        catch (System.Exception ex)
        {
            return new ExecutionResult { Success = false, Message = ex.Message ?? "error", Symbol = decision?.Symbol ?? string.Empty };
        }
    }
}
