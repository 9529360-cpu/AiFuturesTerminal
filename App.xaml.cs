namespace AiFuturesTerminal;

using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AiFuturesTerminal.Core;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.MarketData;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Backtest;
using AiFuturesTerminal.Agent;
using AiFuturesTerminal.UI.ViewModels;
using AiFuturesTerminal.Core.Orchestration;
using AiFuturesTerminal.Core.Logging;
using AiFuturesTerminal.Core.Exchanges.Binance;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Risk;
using System.IO;
using AiFuturesTerminal.Core.Environment;

/// <summary>
/// 应用入口，负责配置依赖注入并启动主窗口。
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _service_provider;

    private void ConfigureServices(IServiceCollection services)
    {
        // 环境配置（测试为 Binance U 本位永续 Testnet）
        services.AddSingleton(new AppEnvironmentOptions
        {
            Mode = EnvironmentMode.BinanceUsdFuturesTestnet,
            ExecutionMode = ExecutionMode.DryRun,
            BinanceUsdFutures = new BinanceUsdFuturesOptions
            {
                // API keys should be provided by user or environment; placeholder kept for dev convenience
                ApiKey = "",
                ApiSecret = "",
                UseTestnet = true,
                BaseAddress = "https://demo-fapi.binance.com"
            }
        });

        // register logging so ILogger<T> can be injected
        services.AddLogging();

        // 注册 IExchangeAdapter：使用工厂根据环境返回实现
        services.AddSingleton<IExchangeAdapter>(sp => ExchangeAdapterFactory.Create(sp.GetRequiredService<AppEnvironmentOptions>()));

        // register trading environment factory and environments
        services.AddSingleton<AiFuturesTerminal.Core.Environment.ITradingEnvironmentFactory, AiFuturesTerminal.Core.Environment.TradingEnvironmentFactory>();

        // register LiveTradingEnvironment dependencies so factory can resolve them
        services.AddSingleton<MarketDataService>();
        services.AddSingleton<AiFuturesTerminal.Core.Analytics.InMemoryTradeBook>();
        services.AddSingleton<IRiskEngine>(sp => new AiFuturesTerminal.Core.Execution.BasicRiskEngine(0.01m));

        // Binance adapter using configured options
        services.AddSingleton<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(sp => new AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter(sp.GetRequiredService<AppEnvironmentOptions>().BinanceUsdFutures, sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>>()));
        services.AddSingleton<AiFuturesTerminal.Core.Exchanges.IBinanceState>(sp => new AiFuturesTerminal.Core.Exchanges.BinanceStateService(sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(), sp.GetRequiredService<AppEnvironmentOptions>(), sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.BinanceStateService>>()));

        // Register BinanceTradeViewService for UI consumption
        services.AddSingleton<Core.Analytics.IBinanceTradeViewService, Core.Analytics.BinanceTradeViewService>();

        // TradeBook for recording trades (in-memory)
        services.AddSingleton<Core.Analytics.SqliteTradeBook>();
        services.AddSingleton<Core.Analytics.InMemoryTradeBook>();
        services.AddSingleton<Core.Analytics.ITradeBook>(sp => new Core.Analytics.ProtectedTradeBook(sp.GetRequiredService<Core.Analytics.SqliteTradeBook>(), sp.GetRequiredService<AppEnvironmentOptions>(), sp.GetService<ILogger<Core.Analytics.ProtectedTradeBook>>()));
        services.AddSingleton<Core.Analytics.InMemoryTradeBook>();

        // register risk bridge and coordinator/guard
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGlobalRiskGuard, GlobalRiskGuard>();
        services.AddSingleton<ITradeBookForRisk, TradeBookRiskBridge>();
        services.AddSingleton<GlobalRiskCoordinator>();

        // register RiskStatusService as IRiskStatusService
        services.AddSingleton<IRiskStatusService, RiskStatusService>();

        // ExecutionEngine requires env options and strategy config; register using factory
        services.AddSingleton<ExecutionEngine>(sp => new ExecutionEngine(
            sp.GetRequiredService<IExchangeAdapter>(),
            sp.GetRequiredService<IRiskEngine>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetRequiredService<StrategyConfig>(),
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetRequiredService<IGlobalRiskGuard>(),
            sp.GetRequiredService<GlobalRiskCoordinator>()));

        services.AddSingleton<BacktestEngine>();
        services.AddSingleton<IStrategyFactory, DefaultStrategyFactory>();
        services.AddSingleton<IBacktestService, BacktestService>();

        // Backtest UI pieces
        services.AddTransient<UI.ViewModels.BacktestViewModel>(sp => new UI.ViewModels.BacktestViewModel(sp.GetRequiredService<IBacktestService>(), sp.GetRequiredService<StrategyConfig>(), sp.GetRequiredService<StrategyWatchConfig>()));
        services.AddTransient<UI.Views.BacktestWindow>();
        services.AddTransient<System.Func<UI.Views.BacktestWindow>>(sp => () => sp.GetRequiredService<UI.Views.BacktestWindow>());
        services.AddTransient<System.Func<UI.ViewModels.BacktestViewModel>>(sp => () => sp.GetRequiredService<UI.ViewModels.BacktestViewModel>());

        // Strategy config service and config
        services.AddSingleton<StrategyConfigService>();
        services.AddSingleton<StrategyConfig>(sp => sp.GetRequiredService<StrategyConfigService>().LoadAsync().GetAwaiter().GetResult());
        services.AddTransient<UI.ViewModels.StrategyConfigViewModel>();
        services.AddTransient<UI.Views.StrategyConfigWindow>();
        services.AddTransient<System.Func<UI.Views.StrategyConfigWindow>>(sp => () => sp.GetRequiredService<UI.Views.StrategyConfigWindow>());

        // Strategy watch config service
        var baseDir = AppContext.BaseDirectory;
        var watchConfigPath = Path.Combine(baseDir, "config", "strategy_watch_config.json");
        var watchConfigService = new StrategyWatchConfigService(watchConfigPath);
        var watchConfig = watchConfigService.LoadOrCreateDefault();
        services.AddSingleton(watchConfigService);
        services.AddSingleton(watchConfig);

        // Agent service registration
        services.AddSingleton<IAgentService, AgentService>();

        // Simple file logger for agent decisions
        services.AddSingleton(new SimpleFileLogger("agent_decisions.log"));

        // Orchestrator
        services.AddSingleton<AgentOrchestrator>();
        services.AddSingleton<IAgentOrchestrator>(sp => sp.GetRequiredService<AgentOrchestrator>());
        services.AddSingleton<AiFuturesTerminal.Core.Environment.ITradingEnvironmentFactory, AiFuturesTerminal.Core.Environment.TradingEnvironmentFactory>();

        // TradeBook UI pieces
        services.AddTransient<UI.ViewModels.TradeBookViewModel>(sp => new UI.ViewModels.TradeBookViewModel(
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetService<Core.Analytics.BinanceTradeViewService>(),
            sp.GetService<AppEnvironmentOptions>(),
            sp.GetService<Core.Analytics.ExchangeFillProcessor>()));
        services.AddTransient<UI.Views.TradeBookWindow>();
        services.AddTransient<System.Func<UI.Views.TradeBookWindow>>(sp => () => sp.GetRequiredService<UI.Views.TradeBookWindow>());

        // UI: Today history and full history viewmodels
        services.AddSingleton<UI.ViewModels.TodayHistoryViewModel>();
        services.AddSingleton<UI.ViewModels.HistoryViewModel>();

        // run log sink
        services.AddSingleton<AiFuturesTerminal.Core.Orchestration.IAgentRunLogSink, AiFuturesTerminal.Core.Orchestration.InMemoryAgentRunLogSink>();

        // Main window & VM registration
        services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(
            sp.GetRequiredService<BacktestEngine>(),
            sp.GetRequiredService<MarketDataService>(),
            sp.GetRequiredService<AgentOrchestrator>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetRequiredService<StrategyConfig>(),
            sp.GetRequiredService<StrategyWatchConfig>(),
            sp.GetRequiredService<StrategyWatchConfigService>(),
            sp.GetRequiredService<ExecutionEngine>(),
            sp.GetRequiredService<IExchangeAdapter>(),
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetRequiredService<GlobalRiskCoordinator>(),
            sp.GetRequiredService<IBacktestService>(),
            sp.GetRequiredService<Func<UI.Views.BacktestWindow>>(),
            sp.GetRequiredService<Func<UI.ViewModels.BacktestViewModel>>(),
            sp.GetRequiredService<ITradingEnvironmentFactory>(),
            sp.GetRequiredService<IAgentService>(),
            sp.GetRequiredService<Func<UI.Views.AnalyticsWindow>>(),
            sp.GetRequiredService<UI.ViewModels.HistoryViewModel>(),
            sp.GetRequiredService<UI.ViewModels.TodayHistoryViewModel>(),
            sp.GetRequiredService<IRiskStatusService>(),
            sp.GetService<Core.Analytics.BinanceTradeSyncService>(),
            sp.GetService<Core.Analytics.BinanceTradeViewService>(),
            sp.GetService<AiFuturesTerminal.Core.Exchanges.IBinanceState>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.Orchestration.IAgentRunLogSink>()
        ));
        services.AddSingleton<MainWindow>();

        // strategies
        services.AddTransient<ScalpingMomentumStrategy>();
        services.AddTransient<TrendFollowingStrategy>();
        services.AddTransient<RangeMeanReversionStrategy>();

        // analytics
        services.AddSingleton<Core.Analytics.BinanceTradeSyncService>(sp => new Core.Analytics.BinanceTradeSyncService(sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>()));
        services.AddSingleton<Core.Analytics.TradeAnalyticsService>(sp => new Core.Analytics.TradeAnalyticsService(
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetService<Core.Analytics.BinanceTradeViewService>()));
        services.AddTransient<UI.ViewModels.AnalyticsViewModel>();
        services.AddTransient<UI.Views.AnalyticsWindow>();
        services.AddTransient<System.Func<UI.Views.AnalyticsWindow>>(sp => () => sp.GetRequiredService<UI.Views.AnalyticsWindow>());

        // register sqlite history store in app dir
        var historyDbPath = Path.Combine(AppContext.BaseDirectory, "data", "history.db");
        services.AddSingleton<AiFuturesTerminal.Core.History.IHistoryStore>(sp => new AiFuturesTerminal.Core.History.SqliteHistoryStore(historyDbPath));
        services.AddSingleton<AiFuturesTerminal.Core.History.IHistorySyncService, AiFuturesTerminal.Core.History.HistorySyncService>();

        // register concrete Binance-backed history services
        services.AddSingleton<AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService(
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService>>()));

        services.AddSingleton<AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService(
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>(),
            sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService>>()));

        // register local concrete services
        services.AddSingleton<AiFuturesTerminal.Core.History.LocalTradeHistoryService>(sp => new AiFuturesTerminal.Core.History.LocalTradeHistoryService(sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>()));
        services.AddSingleton<AiFuturesTerminal.Core.History.LocalOrderHistoryService>(sp => new AiFuturesTerminal.Core.History.LocalOrderHistoryService(sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>()));


        // delegating services choose between Binance and Local based on environment
        services.AddSingleton<AiFuturesTerminal.Core.History.ITradeHistoryService>(sp => new AiFuturesTerminal.Core.History.DelegatingTradeHistoryService(
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.LocalTradeHistoryService>()));

        services.AddSingleton<AiFuturesTerminal.Core.History.IOrderHistoryService>(sp => new AiFuturesTerminal.Core.History.DelegatingOrderHistoryService(
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.LocalOrderHistoryService>()));


        // position history delegates to ITradeHistoryService via BinancePositionHistoryService
        services.AddSingleton<AiFuturesTerminal.Core.History.IPositionHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinancePositionHistoryService(sp.GetRequiredService<AiFuturesTerminal.Core.History.ITradeHistoryService>()));


        // register order routers
        services.AddSingleton<AiFuturesTerminal.Core.Execution.IOrderRouter>(sp =>
        {
            var env = sp.GetRequiredService<AppEnvironmentOptions>();
            if (env.ExecutionMode == Core.Execution.ExecutionMode.DryRun)
            {
                var mockAdapter = sp.GetService<AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter>();
                // ensure there's a mock adapter registered; fall back to creating one
                if (mockAdapter == null)
                {
                    mockAdapter = new AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter();
                }
                return (AiFuturesTerminal.Core.Execution.IOrderRouter)new AiFuturesTerminal.Core.Execution.MockOrderRouter(mockAdapter);
            }

            // default to Binance router if available
            var binAdapter = sp.GetService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>();
            if (binAdapter != null)
                return (AiFuturesTerminal.Core.Execution.IOrderRouter)new AiFuturesTerminal.Core.Execution.BinanceOrderRouter(binAdapter);

            // fallback to mock
            var fallback = new AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter();
            return (AiFuturesTerminal.Core.Execution.IOrderRouter)new AiFuturesTerminal.Core.Execution.MockOrderRouter(fallback);
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure DI service provider is built
        try
        {
            if (_service_provider == null)
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                _service_provider = services.BuildServiceProvider();
            }
        }
        catch (Exception ex)
        {
            // If DI initialization fails, show minimal error and exit
            try { MessageBox.Show("初始化服务失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            Shutdown();
            return;
        }

        // start Binance state service immediately if available
        try
        {
            var svc = _service_provider?.GetService<AiFuturesTerminal.Core.Exchanges.IBinanceState>();
            if (svc != null)
            {
                _ = Task.Run(() => svc.StartAsync(CancellationToken.None));
            }
        }
        catch { }

        // 使用 DI 构造并显示主窗口
        var main = _service_provider.GetRequiredService<MainWindow>();
        main.Show();
    }

}
