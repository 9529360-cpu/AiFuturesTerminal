using System;

namespace AiFuturesTerminal.Core.Exchanges.Binance
{
    public sealed class TradeFillEventArgs : EventArgs
    {
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty; // BUY/SELL
        public string PositionSide { get; init; } = string.Empty; // LONG/SHORT
        public decimal Quantity { get; init; }
        public decimal Price { get; init; }
        public decimal Fee { get; init; }
        public string? FeeAsset { get; init; }
        public decimal RealizedPnl { get; init; }
        public string ExchangeOrderId { get; init; } = string.Empty;
        public string ExchangeTradeId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public bool IsMaker { get; init; }
    }
}
