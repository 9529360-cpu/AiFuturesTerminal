using System;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Orchestration
{
    public sealed class AgentRunContext
    {
        public string StrategyId { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public DateTimeOffset Now { get; init; }
        public MarketSnapshot Market { get; init; } = default!;
        public AccountSnapshot Account { get; init; } = default!;
    }

    public sealed class AgentRunResult
    {
        public IReadOnlyList<PlannedOrder> PlannedOrders { get; init; } = Array.Empty<PlannedOrder>();
        public IReadOnlyList<PlannedOrder> BlockedByRisk { get; init; } = Array.Empty<PlannedOrder>();
        public IReadOnlyList<ExecutionInfo> ExecutedOrders { get; init; } = Array.Empty<ExecutionInfo>();

        public bool IsRiskBlocked => BlockedByRisk.Count > 0;
    }

    public sealed class AgentRunLog
    {
        public DateTimeOffset Timestamp { get; init; }
        public string StrategyId { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;

        public MarketSnapshot Market { get; init; } = default!;
        public AccountSnapshot Account { get; init; } = default!;

        public IReadOnlyList<PlannedOrder> PlannedOrders { get; init; } = Array.Empty<PlannedOrder>();
        public IReadOnlyList<PlannedOrder> BlockedByRisk { get; init; } = Array.Empty<PlannedOrder>();
        public IReadOnlyList<ExecutionInfo> ExecutedOrders { get; init; } = Array.Empty<ExecutionInfo>();
    }

    public sealed class PlannedOrder
    {
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal Price { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public interface IAgentRunLogSink
    {
        void Append(AgentRunLog log);
        IReadOnlyList<AgentRunLog> Snapshot(int maxCount);
    }
}
