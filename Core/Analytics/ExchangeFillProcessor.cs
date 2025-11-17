using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Exchanges.Binance;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Analytics
{
    public sealed class ExchangeFillProcessor : IDisposable
    {
        // lightweight event to notify UI that trades updated on exchange side
        public event EventHandler<string>? TradesUpdated;

        private readonly ITradeBook _tradeBook;
        private readonly AppEnvironmentOptions _envOptions;
        private readonly ILogger<ExchangeFillProcessor>? _logger;
        private readonly BinanceAdapter? _adapter;

        public ExchangeFillProcessor(ITradeBook tradeBook, AppEnvironmentOptions envOptions, BinanceAdapter adapter, ILogger<ExchangeFillProcessor>? logger = null)
        {
            _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
            _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;

            _adapter.TradeFilled += OnTradeFilled;
            // start user data stream in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _adapter.StartUserDataStreamAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to start user data stream");
                }
            });
        }

        private async void OnTradeFilled(object? sender, TradeFillEventArgs e)
        {
            try
            {
                if (_envOptions.ExecutionMode != ExecutionMode.Testnet && _envOptions.ExecutionMode != ExecutionMode.Live)
                {
                    // existing behavior: for simulated/backtest modes, record to tradebook
                    var tradeSim = new TradeRecord
                    {
                        OpenTime = e.Timestamp,
                        CloseTime = e.Timestamp,
                        Symbol = e.Symbol,
                        Side = e.PositionSide.Equals("LONG", StringComparison.OrdinalIgnoreCase) ? TradeSide.Long : TradeSide.Short,
                        Quantity = e.Quantity,
                        EntryPrice = e.Price,
                        ExitPrice = e.Price,
                        RealizedPnl = e.RealizedPnl,
                        Fee = e.Fee,
                        StrategyName = "BinanceManual",
                        Mode = _envOptions.ExecutionMode,
                        ExchangeOrderId = e.ExchangeOrderId,
                        ExchangeTradeId = e.ExchangeTradeId
                    };

                    await _tradeBook.AddAsync(tradeSim, CancellationToken.None).ConfigureAwait(false);
                    _logger?.LogInformation("[Fill] {Mode} {Symbol} {Side} qty={Qty} price={Price} rp={Rp} fee={Fee} orderId={Oid} tradeId={Tid}", _envOptions.ExecutionMode, e.Symbol, e.Side, e.Quantity, e.Price, e.RealizedPnl, e.Fee, e.ExchangeOrderId, e.ExchangeTradeId);
                    return;
                }

                // For Testnet/Live: do not write to ITradeBook. Only log/notify UI for real-time awareness.
                _logger?.LogInformation("【成交回报】{Mode} {Symbol} {Side} 成交 qty={Qty} price={Price} rp={Rp} fee={Fee} orderId={Oid} tradeId={Tid}", _envOptions.ExecutionMode, e.Symbol, e.Side, e.Quantity, e.Price, e.RealizedPnl, e.Fee, e.ExchangeOrderId, e.ExchangeTradeId);
                try { TradesUpdated?.Invoke(this, e.Symbol); } catch { }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing fill event");
            }
        }

        public void Dispose()
        {
            if (_adapter != null)
            {
                try { _adapter.TradeFilled -= OnTradeFilled; } catch { }
            }
        }
    }
}
