using System;

namespace AiFuturesTerminal.Core.Execution
{
    public enum ExecutionInfoKind
    {
        Info,
        OrderPlaced,
        OrderClosed,
        RiskBlocked,
        Error,
        Warning,
        DryRun
    }

    public sealed class ExecutionInfo
    {
        public ExecutionInfoKind Kind { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public DateTime Time { get; init; } = DateTime.UtcNow;

        public ExecutionInfo() { }

        public ExecutionInfo(ExecutionInfoKind kind, string symbol, string message, DateTime? time = null)
        {
            Kind = kind;
            Symbol = symbol ?? string.Empty;
            Message = message ?? string.Empty;
            Time = time ?? DateTime.UtcNow;
        }
    }
}
