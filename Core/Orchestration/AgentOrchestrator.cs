namespace AiFuturesTerminal.Core.Orchestration;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using AiFuturesTerminal.Core.MarketData;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Agent;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Logging;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Environment;

/// <summary>
/// 智能体编排器：协调行情、智能体与执行引擎完成一次决策并执行。
/// 支持多币种轮询与后台循环。
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly MarketDataService _marketData;
    private readonly IExchangeAdapter _exchange;
    private readonly IAgentService _agent;
    private readonly ExecutionEngine _execution;
    private readonly SimpleFileLogger? _logger;
    private readonly StrategyWatchConfig _watchConfig;
    private readonly ILogger<AgentOrchestrator>? _log;
    private readonly ITradingEnvironmentFactory? _envFactory;
    private readonly IAgentRunLogSink? _runLogSink;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public event Action<AiFuturesTerminal.Agent.AgentDecision>? AgentDecisionProduced;

    public AgentOrchestrator(MarketDataService marketData, IExchangeAdapter exchange, IAgentService agent, ExecutionEngine execution, SimpleFileLogger? logger, StrategyWatchConfig watchConfig, ITradingEnvironmentFactory? envFactory = null, IAgentRunLogSink? runLogSink = null, ILogger<AgentOrchestrator>? log = null)
    {
        _marketData = marketData;
        _exchange = exchange;
        _agent = agent;
        _execution = execution;
        _logger = logger;
        _watchConfig = watchConfig;
        _log = log;
        _envFactory = envFactory;
        _runLogSink = runLogSink;
    }

    public async Task RunOnceAsync(string symbol, TimeSpan interval, int historyLimit, CancellationToken ct = default)
    {
        var context = await BuildContextAsync(symbol, interval, historyLimit, ct).ConfigureAwait(false);
        var decision = await _agent.DecideAsync(context, ct).ConfigureAwait(false);

        AgentDecisionProduced?.Invoke(decision);

        try
        {
            _logger?.Log($"{symbol} | {decision.ExecutionDecision.Type} | {decision.StrategyName} | {decision.Confidence} | {decision.Explanation}");
        }
        catch { }

        // map to planned orders
        var planned = new List<PlannedOrder>();
        if (decision.ExecutionDecision != null && decision.ExecutionDecision.Type != ExecutionDecisionType.None)
        {
            planned.Add(decision.ToPlannedOrder());
        }

        // risk check via execution engine (execute for side-effects; detailed per-order blocking is handled by ExecutionEngine events)
        var allowed = new List<PlannedOrder>();
        var blocked = new List<PlannedOrder>();
        var executed = new List<ExecutionInfo>();

        foreach (var p in planned)
        {
            var ed = new ExecutionDecision { Type = p.Side == "Long" ? ExecutionDecisionType.OpenLong : p.Side == "Short" ? ExecutionDecisionType.OpenShort : ExecutionDecisionType.None, Symbol = p.Symbol, EntryPrice = p.Price, Quantity = p.Quantity, Reason = p.Reason };

            try
            {
                // use legacy wrapper to execute via internal environment (we do not inspect result here)
                await _execution.ExecuteAsync(ed, ct).ConfigureAwait(false);
                allowed.Add(p);
                executed.Add(new ExecutionInfo(ExecutionInfoKind.OrderPlaced, p.Symbol, "submitted"));
            }
            catch (Exception ex)
            {
                blocked.Add(p);
                executed.Add(new ExecutionInfo(ExecutionInfoKind.Error, p.Symbol, ex.Message));
            }
        }

        var lastPrice = 0m;
        if (context.History != null && context.History.Count > 0) lastPrice = context.History[context.History.Count - 1].Close;

        var runLog = new AgentRunLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            StrategyId = context.StrategyOverride?.ToString() ?? string.Empty,
            RunId = Guid.NewGuid().ToString(),
            Market = new MarketSnapshot { Symbol = symbol, LastPrice = lastPrice, Timestamp = DateTimeOffset.UtcNow },
            Account = await _exchange.GetAccountSnapshotAsync(ct).ConfigureAwait(false),
            PlannedOrders = planned,
            BlockedByRisk = blocked,
            ExecutedOrders = executed
        };

        try
        {
            _runLogSink?.Append(runLog);
        }
        catch { }
    }

    private async Task<AgentContext> BuildContextAsync(string symbol, TimeSpan interval, int historyLimit, CancellationToken ct)
    {
        var history = await _marketData.LoadHistoricalCandlesAsync(symbol, interval, historyLimit, ct).ConfigureAwait(false);
        var account = await _exchange.GetAccountSnapshotAsync(ct).ConfigureAwait(false);
        var position = await _exchange.GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);

        var cfg = _watchConfig?.Symbols?.FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        var context = new AgentContext
        {
            Symbol = symbol,
            History = history,
            CurrentPosition = position,
            Account = account,
            Now = DateTime.UtcNow,
            StrategyOverride = cfg?.Kind
        };

        return context;
    }

    public void StartLoop()
    {
        if (_loopTask is { IsCompleted: false })
            return;

        _loopCts?.Cancel();
        _loopCts?.Dispose();

        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
    }

    public async Task StopLoopAsync()
    {
        var cts = _loopCts;
        if (cts == null)
            return;

        cts.Cancel();
        try
        {
            if (_loopTask != null)
                await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. 检查是否有持仓（在监控列表内）
                string? openSymbol = null;
                Position? openPosition = null;

                foreach (var s in _watchConfig.Symbols.Where(s => s.Enabled))
                {
                    var pos = await _exchange.GetOpenPositionAsync(s.Symbol, ct).ConfigureAwait(false);
                    if (pos != null && !pos.IsFlat())
                    {
                        openSymbol = s.Symbol;
                        openPosition = pos;
                        break;
                    }
                }

                if (openSymbol != null && openPosition != null)
                {
                    // 有持仓：只对该 symbol 做一次决策（主要是平仓/继续持有）
                    var context = await BuildContextAsync(openSymbol, TimeSpan.FromMinutes(1), 200, ct).ConfigureAwait(false);
                    // 保证 context 中有当前持仓
                    // 触发决策
                    var decision = await _agent.DecideAsync(context, ct).ConfigureAwait(false);
                    AgentDecisionProduced?.Invoke(decision);

                    // map and execute using new RunOnce path
                    await RunInternalOnceAsync(context, decision, ct).ConfigureAwait(false);
                }
                else
                {
                    // 没有持仓：遍历所有启用的 symbol，挑出“最佳开仓信号”
                    AiFuturesTerminal.Agent.AgentDecision? bestDecision = null;
                    string? bestSymbol = null;

                    foreach (var s in _watchConfig.Symbols.Where(s => s.Enabled))
                    {
                        var context = await BuildContextAsync(s.Symbol, TimeSpan.FromMinutes(1), 200, ct).ConfigureAwait(false);
                        var decision = await _agent.DecideAsync(context, ct).ConfigureAwait(false);

                        // 这里只关心“开仓”信号
                        if (decision.ExecutionDecision.Type == ExecutionDecisionType.OpenLong || decision.ExecutionDecision.Type == ExecutionDecisionType.OpenShort)
                        {
                            if (bestDecision == null || decision.Confidence > bestDecision.Confidence)
                            {
                                bestDecision = decision;
                                bestSymbol = s.Symbol;
                            }
                        }
                    }

                    if (bestDecision != null && bestSymbol != null)
                    {
                        AgentDecisionProduced?.Invoke(bestDecision);
                        var ctx = await BuildContextAsync(bestSymbol, TimeSpan.FromMinutes(1), 200, ct).ConfigureAwait(false);
                        await RunInternalOnceAsync(ctx, bestDecision, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "智能体轮询循环发生异常");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }

    // 新增：使用交易环境工厂和 agent.RunOnceAsync 运行循环
    public async Task RunLoopAsync(ExecutionMode mode, TimeSpan interval, CancellationToken ct)
    {
        if (_envFactory == null) throw new InvalidOperationException("未为 AgentOrchestrator 配置 TradingEnvironmentFactory");

        var env = _envFactory.Create(mode);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _agent.RunOnceAsync(env, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Agent RunOnce 异常");
            }

            await Task.Delay(interval, ct).ConfigureAwait(false);
        }
    }

    private async Task RunInternalOnceAsync(AgentContext context, AiFuturesTerminal.Agent.AgentDecision decision, CancellationToken ct)
    {
        // map to planned order
        var planned = new List<PlannedOrder>();
        if (decision.ExecutionDecision != null && decision.ExecutionDecision.Type != ExecutionDecisionType.None)
        {
            planned.Add(decision.ToPlannedOrder());
        }

        var allowed = new List<PlannedOrder>();
        var blocked = new List<PlannedOrder>();
        var executed = new List<ExecutionInfo>();

        foreach (var p in planned)
        {
            var ed = new ExecutionDecision { Type = p.Side == "Long" ? ExecutionDecisionType.OpenLong : p.Side == "Short" ? ExecutionDecisionType.OpenShort : ExecutionDecisionType.None, Symbol = p.Symbol, EntryPrice = p.Price, Quantity = p.Quantity, Reason = p.Reason };
            try
            {
                await _execution.ExecuteAsync(ed, ct).ConfigureAwait(false);
                allowed.Add(p);
                executed.Add(new ExecutionInfo(ExecutionInfoKind.OrderPlaced, p.Symbol, "submitted"));
            }
            catch (Exception ex)
            {
                blocked.Add(p);
                executed.Add(new ExecutionInfo(ExecutionInfoKind.Error, p.Symbol, ex.Message));
            }
        }

        var lastPrice = 0m;
        if (context.History != null && context.History.Count > 0) lastPrice = context.History[context.History.Count - 1].Close;

        var runLog = new AgentRunLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            StrategyId = context.StrategyOverride?.ToString() ?? string.Empty,
            RunId = Guid.NewGuid().ToString(),
            Market = new MarketSnapshot { Symbol = context.Symbol ?? string.Empty, LastPrice = lastPrice, Timestamp = DateTimeOffset.UtcNow },
            Account = context.Account ?? await _exchange.GetAccountSnapshotAsync(ct).ConfigureAwait(false),
            PlannedOrders = planned,
            BlockedByRisk = blocked,
            ExecutedOrders = executed
        };

        try
        {
            _runLogSink?.Append(runLog);
        }
        catch { }
    }
}
