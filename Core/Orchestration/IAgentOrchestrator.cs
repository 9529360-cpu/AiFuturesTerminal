using System;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Orchestration
{
    public interface IAgentOrchestrator
    {
        Task RunOnceAsync(string symbol, TimeSpan interval, int historyLimit, CancellationToken ct = default);
        Task RunLoopAsync(ExecutionMode mode, TimeSpan interval, CancellationToken ct);
        void StartLoop();
        Task StopLoopAsync();
        event Action<AiFuturesTerminal.Agent.AgentDecision>? AgentDecisionProduced;
    }
}
