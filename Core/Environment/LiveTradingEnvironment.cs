namespace AiFuturesTerminal.Core.Environment;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Risk;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Exchanges.Binance;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.MarketData;

public sealed class LiveTradingEnvironment : ITradingEnvironment
{
    private readonly object _sync = new();
    private AccountSnapshot? _currentAccountSnapshot;
    private readonly Dictionary<string, Position> _openPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IExchangeAdapter? _adapter;
    private readonly MarketDataService? _marketDataService;
    private readonly AiFuturesTerminal.Core.Exchanges.IBinanceState? _binanceState;

    // Legacy ctor kept for DI scenarios that previously constructed this type
    public LiveTradingEnvironment(ITradeBook? tradeBook = null, IRiskEngine? riskEngine = null, IOrderRouter? orderRouter = null)
    {
        TradeBook = tradeBook ?? new AiFuturesTerminal.Core.Analytics.InMemoryTradeBook();
        RiskEngine = riskEngine ?? new AiFuturesTerminal.Core.Execution.BasicRiskEngine(0.01m);

        // If caller didn't provide an OrderRouter, try to create a BinanceOrderRouter with a default adapter options.
        OrderRouter = orderRouter ?? new AiFuturesTerminal.Core.Execution.BinanceOrderRouter(new BinanceAdapter(new BinanceUsdFuturesOptions()));

        // initialize default account snapshot (can be refreshed later)
        _currentAccountSnapshot = new AccountSnapshot(0m, 0m, DateTime.UtcNow);
    }

    // New ctor used by factory: provide adapter and market data service along with other dependencies
    public LiveTradingEnvironment(IExchangeAdapter adapter, MarketDataService marketDataService, ITradeBook tradeBook, IRiskEngine riskEngine, IOrderRouter orderRouter, AiFuturesTerminal.Core.Exchanges.IBinanceState? binanceState = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        TradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
        RiskEngine = riskEngine ?? throw new ArgumentNullException(nameof(riskEngine));
        OrderRouter = orderRouter ?? throw new ArgumentNullException(nameof(orderRouter));
        _binanceState = binanceState;

        // initialize default account snapshot (will be refreshed on demand via adapter)
        _currentAccountSnapshot = null;
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public ITradeBook TradeBook { get; }

    public IRiskEngine RiskEngine { get; }

    public IOrderRouter OrderRouter { get; }

    public Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // If BinanceState is available prefer it (authoritative for Testnet/Live)
        if (_binanceState != null)
        {
            try
            {
                var dtoTask = _binanceState.GetAccountAsync(cancellationToken);
                if (dtoTask != null)
                {
                    var dto = dtoTask.GetAwaiter().GetResult();
                    var snap = new AccountSnapshot(dto.Equity, dto.FreeBalance, dto.Timestamp);
                    lock (_sync) { _currentAccountSnapshot = snap; }
                    return Task.FromResult(snap);
                }
            }
            catch
            {
                // fallback to adapter/cached
            }
        }

        // Always try adapter first as fallback
        if (_adapter != null)
        {
            try
            {
                var snapTask = _adapter.GetAccountSnapshotAsync(cancellationToken);
                if (snapTask != null)
                {
                    var snap = snapTask.GetAwaiter().GetResult();
                    lock (_sync) { _currentAccountSnapshot = snap; }
                    return Task.FromResult(snap);
                }
            }
            catch
            {
                // fallback to cached
            }
        }

        lock (_sync)
        {
            return Task.FromResult(_currentAccountSnapshot ?? new AccountSnapshot(0m, 0m, DateTime.UtcNow));
        }
    }

    public async Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        // Prefer BinanceState when available (Testnet/Live authoritative source)
        if (_binanceState != null)
        {
            try
            {
                var dto = await _binanceState.GetOpenPositionAsync(symbol, cancellationToken).ConfigureAwait(false);
                if (dto != null)
                {
                    var pos = new Position(dto.Symbol)
                    {
                        Side = dto.Side,
                        Quantity = dto.Quantity,
                        EntryPrice = dto.EntryPrice,
                        EntryTime = dto.EntryTime
                    };
                    UpdateOpenPosition(pos);
                    return pos;
                }
                return null;
            }
            catch
            {
                // fallback to adapter/cached
            }
        }

        // Fallback to adapter if available
        if (_adapter != null)
        {
            try
            {
                var binAdapter = _adapter as AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter;
                if (binAdapter != null)
                {
                    var pos = await binAdapter.GetOpenPositionAsync(symbol, cancellationToken).ConfigureAwait(false);
                    if (pos != null)
                    {
                        UpdateOpenPosition(pos);
                        return pos;
                    }
                    return null;
                }

                var genericPos = await _adapter.GetOpenPositionAsync(symbol, cancellationToken).ConfigureAwait(false);
                if (genericPos != null)
                {
                    UpdateOpenPosition(genericPos);
                    return genericPos;
                }
                return null;
            }
            catch
            {
                // fallback to cached
            }
        }

        lock (_sync)
        {
            _openPositions.TryGetValue(symbol.ToUpperInvariant(), out var pos);
            return pos;
        }
    }

    public async Task<Candle[]> GetRecentCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
    {
        if (_adapter != null)
        {
            try
            {
                var list = await _adapter.GetHistoricalCandlesAsync(symbol, interval, limit, ct).ConfigureAwait(false);
                return list?.ToArray() ?? Array.Empty<Candle>();
            }
            catch
            {
                // fall through to market data service
            }
        }

        if (_marketDataService != null)
        {
            try
            {
                // MarketDataService exposes LoadHistoricalCandlesAsync
                var list = _marketDataService.LoadHistoricalCandlesAsync(symbol, interval, limit, ct).GetAwaiter().GetResult();
                return list?.ToArray() ?? Array.Empty<Candle>();
            }
            catch
            {
                // swallow and fallback
            }
        }

        return Array.Empty<Candle>();
    }

    // Optional helper: allow updating cached account/position from external callers
    public void UpdateAccountSnapshot(AccountSnapshot snapshot)
    {
        if (snapshot == null) return;
        lock (_sync)
        {
            _currentAccountSnapshot = snapshot;
        }
    }

    public void UpdateOpenPosition(Position? position)
    {
        if (position == null) return;
        lock (_sync)
        {
            if (position.IsFlat())
            {
                _openPositions.Remove(position.Symbol);
            }
            else
            {
                _openPositions[position.Symbol.ToUpperInvariant()] = position;
            }
        }
    }
}
