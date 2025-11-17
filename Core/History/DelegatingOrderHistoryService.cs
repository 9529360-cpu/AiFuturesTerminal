namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Environment;

public sealed class DelegatingOrderHistoryService : IOrderHistoryService
{
    private readonly AppEnvironmentOptions _env;
    private readonly IOrderHistoryService _binanceService;
    private readonly IOrderHistoryService _localService;

    public DelegatingOrderHistoryService(AppEnvironmentOptions env, IOrderHistoryService binanceService, IOrderHistoryService localService)
    {
        _env = env;
        _binanceService = binanceService;
        _localService = localService;
    }

    public Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default)
    {
        if (_env.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.Testnet || _env.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.Live)
            return _binanceService.QueryOrdersAsync(query, ct);
        return _localService.QueryOrdersAsync(query, ct);
    }
}
