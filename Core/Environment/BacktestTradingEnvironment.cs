namespace AiFuturesTerminal.Core.Environment;

using System;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Exchanges.Mock;
using System.Linq;

public sealed class BacktestTradingEnvironment : ITradingEnvironment
{
    private DateTimeOffset _now;
    private readonly MockExchangeAdapter? _mockAdapter;

    public BacktestTradingEnvironment(DateTimeOffset initialNow, ITradeBook? tradeBook = null, IRiskEngine? riskEngine = null, IOrderRouter? orderRouter = null)
    {
        _now = initialNow;
        TradeBook = tradeBook ?? new AiFuturesTerminal.Core.Analytics.InMemoryTradeBook();
        RiskEngine = riskEngine ?? new AiFuturesTerminal.Core.Execution.BasicRiskEngine(0.01m);

        if (orderRouter != null)
        {
            OrderRouter = orderRouter;
            _mockAdapter = null;
        }
        else
        {
            // create a mock adapter and router and keep adapter reference so GetOpenPositionAsync can reflect router state
            _mockAdapter = new MockExchangeAdapter();
            OrderRouter = new AiFuturesTerminal.Core.Execution.MockOrderRouter(_mockAdapter);
        }
    }

    public DateTimeOffset UtcNow => _now;

    public ITradeBook TradeBook { get; }

    public IRiskEngine RiskEngine { get; }

    public IOrderRouter OrderRouter { get; }

    public void AdvanceTo(DateTimeOffset newNow)
    {
        _now = newNow;
    }

    public Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new AccountSnapshot(TradeBook.GetAllTrades().Sum(t => t.RealizedPnl) + 1000m, 1000m, DateTime.UtcNow));
    }

    public Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default)
    {
        if (_mockAdapter != null)
        {
            return _mockAdapter.GetOpenPositionAsync(symbol, ct);
        }

        // Fallback: no adapter available (external OrderRouter manages state) - return null
        return Task.FromResult<Position?>(null);
    }

    public void UpdateMarketPrice(string symbol, decimal price)
    {
        try
        {
            _mockAdapter?.SetLastPrice(symbol, price);
        }
        catch { }
    }

    // Provide recent candles for backtest environment by delegating to mock adapter when available
    public async Task<Candle[]> GetRecentCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
    {
        if (_mockAdapter != null)
        {
            try
            {
                var list = await _mockAdapter.GetHistoricalCandlesAsync(symbol, interval, limit, ct).ConfigureAwait(false);
                return list?.ToArray() ?? Array.Empty<Candle>();
            }
            catch
            {
                // swallow and return empty
            }
        }

        return Array.Empty<Candle>();
    }
}
