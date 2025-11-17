namespace AiFuturesTerminal.Core.Environment;

using System;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

public interface ITradingEnvironment
{
    DateTimeOffset UtcNow { get; }
    ITradeBook TradeBook { get; }
    IRiskEngine RiskEngine { get; }
    IOrderRouter OrderRouter { get; }

    // Provide ways to query current account snapshot and open position in this environment
    Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default);
    Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default);

    // Retrieve recent candles for symbol (used by live/dryrun/backtest)
    Task<Candle[]> GetRecentCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default);
}
