using System;
using AiFuturesTerminal.Core.Analytics;

namespace AiFuturesTerminal.Core.Risk
{
    /// <summary>
    /// Bridge between existing ITradeBook and the risk coordinator's ITradeBookForRisk.
    /// Subscribes to TradeRecorded and forwards a simplified TradeClosedEventArgs.
    /// </summary>
    public sealed class TradeBookRiskBridge : ITradeBookForRisk, IDisposable
    {
        private readonly ITradeBook _tradeBook;

        public event EventHandler<TradeClosedEventArgs>? TradeClosed;

        public TradeBookRiskBridge(ITradeBook tradeBook)
        {
            _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
            _tradeBook.TradeRecorded += OnTradeRecorded;
        }

        private void OnTradeRecorded(object? sender, TradeRecord e)
        {
            try
            {
                var args = new TradeClosedEventArgs
                {
                    Time = e.CloseTime,
                    Symbol = e.Symbol,
                    Pnl = e.RealizedPnl
                };

                TradeClosed?.Invoke(this, args);
            }
            catch
            {
                // swallow - risk bridge should not throw
            }
        }

        public void Dispose()
        {
            _tradeBook.TradeRecorded -= OnTradeRecorded;
        }
    }
}
