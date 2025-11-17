using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiFuturesTerminal.Core.Analytics
{
    /// <summary>
    /// Wrapper around a concrete ITradeBook that prevents writes when running in Testnet/Live modes.
    /// Ensures only Backtest/DryRun can write into the local tradebook.
    /// </summary>
    public sealed class ProtectedTradeBook : ITradeBook
    {
        private readonly ITradeBook _inner;
        private readonly AiFuturesTerminal.Core.AppEnvironmentOptions _envOptions;
        private readonly ILogger<ProtectedTradeBook>? _logger;

        public event EventHandler<TradeRecord>? TradeRecorded
        {
            add { _inner.TradeRecorded += value; }
            remove { _inner.TradeRecorded -= value; }
        }

        public ProtectedTradeBook(ITradeBook inner, AiFuturesTerminal.Core.AppEnvironmentOptions envOptions, ILogger<ProtectedTradeBook>? logger = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
            _logger = logger;
        }

        private bool WritesAllowed => _envOptions.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.Backtest
                                      || _envOptions.ExecutionMode == AiFuturesTerminal.Core.Execution.ExecutionMode.DryRun;

        /// <summary>
        /// True when local tradebook writes are enabled (Backtest or DryRun).
        /// False for Testnet/Live where we avoid writing real fills to local DB.
        /// </summary>
        public bool IsWriteEnabled => WritesAllowed;

        public void AddTrade(TradeRecord trade)
        {
            if (WritesAllowed)
            {
                _inner.AddTrade(trade);
                return;
            }

            try { _logger?.LogWarning("ProtectedTradeBook: blocked AddTrade in mode {Mode} for symbol {Symbol}", _envOptions.ExecutionMode, trade.Symbol); } catch { }
        }

        public Task AddAsync(TradeRecord trade, CancellationToken ct = default)
        {
            if (WritesAllowed)
            {
                return _inner.AddAsync(trade, ct);
            }

            try { _logger?.LogWarning("ProtectedTradeBook: blocked AddAsync in mode {Mode} for symbol {Symbol}", _envOptions.ExecutionMode, trade.Symbol); } catch { }
            return Task.CompletedTask;
        }

        public IReadOnlyList<TradeRecord> GetTrades(DateOnly date)
        {
            return _inner.GetTrades(date);
        }

        public Task<IReadOnlyList<TradeRecord>> GetTradesAsync(DateTime? from = null, DateTime? to = null, string? strategyName = null, CancellationToken ct = default)
        {
            return _inner.GetTradesAsync(from, to, strategyName, ct);
        }

        public DailyTradeSummary GetDailySummary(DateOnly date)
        {
            return _inner.GetDailySummary(date);
        }

        public IReadOnlyList<TradeRecord> GetAllTrades()
        {
            // If writes are blocked and we want to avoid exposing stale local records for Testnet/Live,
            // callers should use IBinanceState / BinanceTradeViewService. Still return underlying for compatibility.
            return _inner.GetAllTrades();
        }

        public Task<IReadOnlyList<TradeRecord>> GetAllTradesAsync(CancellationToken ct = default)
        {
            return _inner.GetAllTradesAsync(ct);
        }
    }
}
