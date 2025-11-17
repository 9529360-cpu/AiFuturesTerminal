namespace AiFuturesTerminal.Core.History;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Environment;

public sealed class DelegatingTradeHistoryService : ITradeHistoryService
{
    private readonly AppEnvironmentOptions _env;
    private readonly ITradeHistoryService _binanceService;
    private readonly ITradeHistoryService _localService;

    public DelegatingTradeHistoryService(AppEnvironmentOptions env, ITradeHistoryService binanceService, ITradeHistoryService localService)
    {
        _env = env;
        _binanceService = binanceService;
        _localService = localService;
    }

    public Task<IReadOnlyList<TradeHistoryRecord>> QueryTradesAsync(HistoryQuery query, CancellationToken ct = default)
    {
        if (_env.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.Testnet || _env.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.Live)
            return _binanceService.QueryTradesAsync(query, ct);
        return _localService.QueryTradesAsync(query, ct);
    }
}
