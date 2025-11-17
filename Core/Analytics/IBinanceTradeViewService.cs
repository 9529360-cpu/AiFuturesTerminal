using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AiFuturesTerminal.Core.Analytics
{
    public interface IBinanceTradeViewService
    {
        Task<IReadOnlyList<TradeRecord>> GetTodayTradeRecordsAsync(string? symbol, CancellationToken ct = default);
        Task<IReadOnlyList<UiTodayTradeRow>> GetTodayTradesAsync(string? symbol = null, CancellationToken ct = default);
        Task<TodaySummaryDto> GetTodaySummaryAsync(string? symbol = null, CancellationToken ct = default);
    }
}