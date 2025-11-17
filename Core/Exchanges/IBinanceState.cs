using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Models;

namespace AiFuturesTerminal.Core.Exchanges
{
    // DTOs used by BinanceState
    public sealed class AccountSnapshotDto
    {
        public decimal Equity { get; init; }
        public decimal FreeBalance { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public sealed class PositionDto
    {
        public string Symbol { get; init; } = string.Empty;
        public PositionSide Side { get; init; }
        public decimal Quantity { get; init; }
        public decimal EntryPrice { get; init; }
        public DateTime? EntryTime { get; init; }

        // 新增：标记价，用于计算名义价值
        public decimal MarkPrice { get; set; }

        /// <summary>名义仓位价值（约等于币安 UI 的“数量(USDT)”）</summary>
        public decimal NotionalUsdt { get; set; }

        /// <summary>未实现盈亏（USDT），来源于 unRealizedProfit 或计算得到</summary>
        public decimal UnrealizedPnlUsdt { get; set; }
    }

    public sealed class OrderDto
    {
        public string Symbol { get; init; } = string.Empty;
        public string OrderId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public decimal Quantity { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public sealed class DailyPnlRow
    {
        public DateTime Date { get; init; }
        public decimal RealizedPnl { get; init; }
        public decimal Commission { get; init; }
        public string Symbol { get; init; } = string.Empty;
    }

    public sealed class PlaceOrderRequest
    {
        public string Symbol { get; init; } = string.Empty;
        public PositionSide Side { get; init; }
        public decimal Quantity { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class ClosePositionRequest
    {
        public string Symbol { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    public interface IBinanceState : IDisposable
    {
        // lifecycle
        Task StartAsync(CancellationToken ct = default);

        // readonly queries
        Task<AccountSnapshotDto> GetAccountAsync(CancellationToken ct = default);
        Task<IReadOnlyList<PositionDto>> GetOpenPositionsAsync(CancellationToken ct = default);
        Task<PositionDto?> GetOpenPositionAsync(string symbol, CancellationToken ct = default);
        Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(string? symbol, CancellationToken ct = default);
        Task<IReadOnlyList<TradeRecord>> GetRecentTradesAsync(DateTime from, DateTime to, string? symbol, CancellationToken ct = default);
        Task<IReadOnlyList<DailyPnlRow>> GetDailyPnlAsync(DateTime from, DateTime to, string? symbol, CancellationToken ct = default);

        // snapshot + events for UI consumption
        IReadOnlyList<PositionDto> GetOpenPositionsSnapshot();
        event EventHandler? PositionsChanged;

        // manual refresh
        Task RefreshPositionsAsync(CancellationToken ct = default);

        // actions
        Task PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct = default);
        Task ClosePositionAsync(ClosePositionRequest req, CancellationToken ct = default);
    }
}
