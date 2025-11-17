namespace AiFuturesTerminal.Core.Environment;

using System;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.MarketData;
using Microsoft.Extensions.DependencyInjection;

public interface ITradingEnvironmentFactory
{
    ITradingEnvironment Create(ExecutionMode mode);
}

public sealed class TradingEnvironmentFactory : ITradingEnvironmentFactory
{
    private readonly IServiceProvider _sp;
    private readonly AppEnvironmentOptions _envOptions;

    public TradingEnvironmentFactory(IServiceProvider sp, AppEnvironmentOptions envOptions)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
    }

    public ITradingEnvironment Create(ExecutionMode mode)
    {
        // For DryRun we create a LiveTradingEnvironment backed by mock adapter/router
        if (mode == ExecutionMode.DryRun)
        {
            var adapter = _sp.GetService<IExchangeAdapter>() ?? throw new InvalidOperationException("IExchangeAdapter not registered");
            var marketData = _sp.GetService<MarketDataService>() ?? throw new InvalidOperationException("MarketDataService not registered");
            // for DryRun use an in-memory tradebook and mock router
            var tradeBook = _sp.GetService<AiFuturesTerminal.Core.Analytics.InMemoryTradeBook>() ?? new AiFuturesTerminal.Core.Analytics.InMemoryTradeBook();
            var riskEngine = _sp.GetService<IRiskEngine>() ?? new AiFuturesTerminal.Core.Execution.BasicRiskEngine(0.01m);
            var orderRouter = new AiFuturesTerminal.Core.Execution.MockOrderRouter(adapter as AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter ?? new AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter());

            return new LiveTradingEnvironment(adapter, marketData, tradeBook, riskEngine, orderRouter);
        }

        // For Testnet/Live use BinanceOrderRouter and the app-wide tradebook (persistent)
        if (mode == ExecutionMode.Testnet || mode == ExecutionMode.Live)
        {
            var adapter = _sp.GetService<IExchangeAdapter>() ?? throw new InvalidOperationException("IExchangeAdapter not registered");
            var marketData = _sp.GetService<MarketDataService>() ?? throw new InvalidOperationException("MarketDataService not registered");
            var tradeBook = _sp.GetService<ITradeBook>() ?? new AiFuturesTerminal.Core.Analytics.InMemoryTradeBook();
            var riskEngine = _sp.GetService<IRiskEngine>() ?? new AiFuturesTerminal.Core.Execution.BasicRiskEngine(0.01m);

            // prefer concrete BinanceAdapter if available
            AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter? binAdapter = adapter as AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter;
            AiFuturesTerminal.Core.Execution.IOrderRouter orderRouter;
            if (binAdapter != null)
            {
                orderRouter = new AiFuturesTerminal.Core.Execution.BinanceOrderRouter(binAdapter);
            }
            else
            {
                // fallback: try to get a registered BinanceAdapter from DI
                binAdapter = _sp.GetService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>();
                if (binAdapter != null)
                    orderRouter = new AiFuturesTerminal.Core.Execution.BinanceOrderRouter(binAdapter);
                else
                    throw new InvalidOperationException("Binance adapter not available for Testnet/Live environment");
            }

            var env = new LiveTradingEnvironment(adapter, marketData, tradeBook, riskEngine, orderRouter);
            // log environment creation
            try { Console.WriteLine($"[Env] mode={mode}, router={orderRouter.GetType().Name}"); } catch { }
            return env;
        }

        // Fallback: create a BacktestTradingEnvironment with current time
        return new BacktestTradingEnvironment(DateTimeOffset.UtcNow);
    }
}
