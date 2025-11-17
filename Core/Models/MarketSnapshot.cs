using System;

namespace AiFuturesTerminal.Core.Models
{
    public sealed class MarketSnapshot
    {
        public string Symbol { get; init; } = string.Empty;
        public decimal LastPrice { get; init; }
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }
}
