using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Environment;

namespace AiFuturesTerminal.Agent
{
    /// <summary>
    /// Interface for an AI agent service that can produce decisions based on market data.
    /// Implementations may wrap local AI models or remote inference services.
    /// </summary>
    public interface IAgentService
    {
        /// <summary>
        /// Produce an agent decision based on given context (single-symbol).
        /// </summary>
        Task<AgentDecision> DecideAsync(AgentContext context, CancellationToken ct = default);

        /// <summary>
        /// Run a single pass of agent over symbols using provided trading environment (fetch candles, decide, execute).
        /// </summary>
        Task RunOnceAsync(ITradingEnvironment env, CancellationToken ct = default);
    }
}
