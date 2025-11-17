using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AiFuturesTerminal.Agent;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

namespace AiFuturesTerminal.Core.Orchestration
{
    public static class AgentOrchestratorExtensions
    {
        // Helper to map AgentDecision -> PlannedOrder
        public static PlannedOrder ToPlannedOrder(this AiFuturesTerminal.Agent.AgentDecision decision)
        {
            var ed = decision.ExecutionDecision;
            return new PlannedOrder
            {
                Symbol = ed.Symbol ?? string.Empty,
                Side = ed.Type == ExecutionDecisionType.OpenLong ? "Long" : ed.Type == ExecutionDecisionType.OpenShort ? "Short" : "None",
                Quantity = ed.Quantity ?? 0m,
                Price = ed.EntryPrice ?? ed.LastPrice ?? 0m,
                Reason = ed.Reason ?? string.Empty
            };
        }
    }
}
