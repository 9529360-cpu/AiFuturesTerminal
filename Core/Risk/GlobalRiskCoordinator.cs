using System;

namespace AiFuturesTerminal.Core.Risk
{
    public sealed record GlobalRiskSnapshot(
        DateOnly TradingDate,
        int TradesToday,
        int? MaxTradesPerDay,
        int ConsecutiveLossCount,
        int? MaxConsecutiveLoss,
        bool IsFrozen,
        bool IsManualFrozen,
        string? FrozenReason
    );

    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public sealed class TradeClosedEventArgs : EventArgs
    {
        public DateTime Time { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public decimal Pnl { get; init; }
    }

    public interface ITradeBookForRisk
    {
        event EventHandler<TradeClosedEventArgs>? TradeClosed;
    }

    public sealed class GlobalRiskCoordinator
    {
        private readonly GlobalRiskRuntime _runtime = new();
        private readonly IClock _clock;

        public GlobalRiskRuntime Runtime => _runtime;

        public GlobalRiskCoordinator(ITradeBookForRisk tradeBook, IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _runtime.ResetFor(DateOnly.FromDateTime(_clock.UtcNow));

            tradeBook.TradeClosed += OnTradeClosed;
        }

        private void OnTradeClosed(object? sender, TradeClosedEventArgs e)
        {
            var tradeDate = DateOnly.FromDateTime(e.Time);

            if (tradeDate != _runtime.TradingDate)
            {
                _runtime.ResetFor(tradeDate);
            }

            _runtime.OnTradeClosed(e.Pnl);
        }

        // UI can call this to enable/disable manual Kill Switch
        public void SetKillSwitch(bool enabled, string? reason = null)
        {
            if (enabled)
                _runtime.FreezeManually(reason);
            else
                _runtime.UnfreezeManually();
        }

        // Provide a snapshot for UI consumption
        public GlobalRiskSnapshot GetSnapshot(GlobalRiskSettings settings)
        {
            var r = _runtime;
            return new GlobalRiskSnapshot(
                r.TradingDate,
                r.TradesToday,
                settings.MaxTradesPerDay > 0 ? settings.MaxTradesPerDay : (int?)null,
                r.ConsecutiveLossCount,
                settings.MaxConsecutiveLoss > 0 ? settings.MaxConsecutiveLoss : (int?)null,
                r.IsFrozen,
                r.IsManualFrozen,
                r.FrozenReason
            );
        }
    }
}
