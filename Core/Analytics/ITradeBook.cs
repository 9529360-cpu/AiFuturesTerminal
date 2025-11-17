using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AiFuturesTerminal.Core.Analytics;

public interface ITradeBook
{
    event EventHandler<TradeRecord>? TradeRecorded;

    void AddTrade(TradeRecord trade);

    IReadOnlyList<TradeRecord> GetTrades(DateOnly date);

    DailyTradeSummary GetDailySummary(DateOnly date);

    IReadOnlyList<TradeRecord> GetAllTrades();

    // Async APIs for persistent implementations
    Task AddAsync(TradeRecord trade, CancellationToken ct = default);
    Task<IReadOnlyList<TradeRecord>> GetTradesAsync(DateTime? from = null, DateTime? to = null, string? strategyName = null, CancellationToken ct = default);
    Task<IReadOnlyList<TradeRecord>> GetAllTradesAsync(CancellationToken ct = default);
}
