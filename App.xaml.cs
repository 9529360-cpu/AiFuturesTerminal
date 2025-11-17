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
                // Testnet API Key / Secret provided by user (kept as configured)
                ApiKey = "Q7zPHbQERp4Fgkck4lBR8fqt94ObSnKduUvBi95KmKQYfq1kdNSo7PwJQ5Yb4Vhq",
                ApiSecret = "m998RrkhHmBn8Cwz5SWYYdyTOuRMq6SiHLQqKZcKArlkP3KhQBw4MI8P1ZqKTSJY",
                // 保持 Testnet 环境
                UseTestnet = true,
                // 使用 U 本位永续 Futures Testnet 官方域名
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
        var envOpts = services.BuildServiceProvider().GetService<AiFuturesTerminal.Core.AppEnvironmentOptions>();
        if (envOpts != null)
        {
            services.AddSingleton<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(sp => new AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter(envOpts.BinanceUsdFutures, sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>>()));
        }
        else
        {
            // fallback to default options
            services.AddSingleton<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(sp => new AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter(new AiFuturesTerminal.Core.Exchanges.Binance.BinanceUsdFuturesOptions(), sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>>()));
        }
        services.AddSingleton<AiFuturesTerminal.Core.Exchanges.IBinanceState>(sp => new AiFuturesTerminal.Core.Exchanges.BinanceStateService(sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(), sp.GetRequiredService<AppEnvironmentOptions>(), sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.BinanceStateService>>()));

        // Register BinanceTradeViewService for UI consumption
        services.AddSingleton<Core.Analytics.IBinanceTradeViewService, Core.Analytics.BinanceTradeViewService>();

        // Do not register a global IOrderRouter here. TradingEnvironmentFactory will provide appropriate router per ExecutionMode:
        // - Backtest/DryRun -> MockOrderRouter
        // - Testnet/Live  -> BinanceOrderRouter

        // TradeBook for recording trades (in-memory)
        // register persistent sqlite tradebook and expose as ITradeBook via ProtectedTradeBook to prevent writes in Testnet/Live
        services.AddSingleton<Core.Analytics.SqliteTradeBook>();
        services.AddSingleton<Core.Analytics.InMemoryTradeBook>();
        services.AddSingleton<Core.Analytics.ITradeBook>(sp => new Core.Analytics.ProtectedTradeBook(sp.GetRequiredService<Core.Analytics.SqliteTradeBook>(), sp.GetRequiredService<AppEnvironmentOptions>(), sp.GetService<Microsoft.Extensions.Logging.ILogger<Core.Analytics.ProtectedTradeBook>>()));
        // also register an InMemoryTradeBook for internal backtest engines if needed
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

        // Backtest UI pieces - adjust BacktestViewModel registration to include watchConfig
        services.AddTransient<UI.ViewModels.BacktestViewModel>(sp => new UI.ViewModels.BacktestViewModel(sp.GetRequiredService<IBacktestService>(), sp.GetRequiredService<StrategyConfig>(), sp.GetRequiredService<StrategyWatchConfig>()));
        services.AddTransient<UI.Views.BacktestWindow>();
        services.AddTransient<System.Func<UI.Views.BacktestWindow>>(sp => () => sp.GetRequiredService<UI.Views.BacktestWindow>());
        services.AddTransient<System.Func<UI.ViewModels.BacktestViewModel>>(sp => () => sp.GetRequiredService<UI.ViewModels.BacktestViewModel>());

        // Strategy config service and config
        services.AddSingleton<StrategyConfigService>();
        services.AddSingleton<StrategyConfig>(sp => sp.GetRequiredService<StrategyConfigService>().LoadAsync().GetAwaiter().GetResult());
        // StrategyConfig UI pieces
        services.AddTransient<UI.ViewModels.StrategyConfigViewModel>();
        services.AddTransient<UI.Views.StrategyConfigWindow>();
        // register factory for creating StrategyConfigWindow instances (for reuse after close)
        services.AddTransient<System.Func<UI.Views.StrategyConfigWindow>>(sp => () => sp.GetRequiredService<UI.Views.StrategyConfigWindow>());

        // Strategy watch config service and config (persisted JSON)
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

        // Register orchestrator and inject logger via constructor (or property)
        services.AddSingleton<AgentOrchestrator>();
        // expose orchestrator via interface for VM (optional)
        services.AddSingleton<IAgentOrchestrator>(sp => sp.GetRequiredService<AgentOrchestrator>());
        // register trading environment factory so MainWindowViewModel can create envs
        services.AddSingleton<AiFuturesTerminal.Core.Environment.ITradingEnvironmentFactory, AiFuturesTerminal.Core.Environment.TradingEnvironmentFactory>();

        // TradeBook UI pieces
        services.AddTransient<UI.ViewModels.TradeBookViewModel>(sp => new UI.ViewModels.TradeBookViewModel(
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetService<Core.Analytics.BinanceTradeViewService>(),
            sp.GetService<AppEnvironmentOptions>(),
            sp.GetService<Core.Analytics.ExchangeFillProcessor>()));
        services.AddTransient<UI.Views.TradeBookWindow>();
        services.AddTransient<System.Func<UI.Views.TradeBookWindow>>(sp => () => sp.GetRequiredService<UI.Views.TradeBookWindow>());

        // UI: Today history viewmodel
        services.AddSingleton<UI.ViewModels.TodayHistoryViewModel>();
        // UI: full history viewmodel
        services.AddSingleton<UI.ViewModels.HistoryViewModel>();

        // register run log sink
        services.AddSingleton<AiFuturesTerminal.Core.Orchestration.IAgentRunLogSink, AiFuturesTerminal.Core.Orchestration.InMemoryAgentRunLogSink>();

        // register MainWindowViewModel using factory to provide HistoryViewModel
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

        // register strategy implementations so they can be resolved by DI if needed
        services.AddTransient<ScalpingMomentumStrategy>();
        services.AddTransient<TrendFollowingStrategy>();
        services.AddTransient<RangeMeanReversionStrategy>();

        // Analytics service
        // Keep BinanceTradeSyncService available for debug/legacy usage but use BinanceTradeViewService as authoritative view for UI
        services.AddSingleton<Core.Analytics.BinanceTradeSyncService>(sp => new Core.Analytics.BinanceTradeSyncService(sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>()));
        // TradeAnalyticsService will use BinanceTradeViewService when in Testnet/Live
        services.AddSingleton<Core.Analytics.TradeAnalyticsService>(sp => new Core.Analytics.TradeAnalyticsService(
            sp.GetRequiredService<Core.Analytics.ITradeBook>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetService<Core.Analytics.BinanceTradeViewService>()));
        services.AddTransient<UI.ViewModels.AnalyticsViewModel>();
        services.AddTransient<UI.Views.AnalyticsWindow>();
        // factory for AnalyticsWindow for injection into MainWindowViewModel
        services.AddTransient<System.Func<UI.Views.AnalyticsWindow>>(sp => () => sp.GetRequiredService<UI.Views.AnalyticsWindow>());

        // register history services (Binance-backed implementations)
        services.AddSingleton<AiFuturesTerminal.Core.History.ITradeHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService(
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>(),
            sp.GetRequiredService<AppEnvironmentOptions>(),
            sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.History.BinanceTradeHistoryService>>()));

        services.AddSingleton<AiFuturesTerminal.Core.History.IOrderHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService(
            sp.GetRequiredService<AiFuturesTerminal.Core.Exchanges.Binance.BinanceAdapter>(),
            sp.GetRequiredService<AiFuturesTerminal.Core.History.IHistoryStore>(),
            sp.GetService<ILogger<AiFuturesTerminal.Core.Exchanges.History.BinanceOrderHistoryService>>()));

        services.AddSingleton<AiFuturesTerminal.Core.History.IPositionHistoryService>(sp => new AiFuturesTerminal.Core.Exchanges.History.BinancePositionHistoryService(sp.GetRequiredService<AiFuturesTerminal.Core.History.ITradeHistoryService>()));

        // register sqlite history store in app dir
        var historyDbPath = Path.Combine(AppContext.BaseDirectory, "data", "history.db");
        services.AddSingleton<AiFuturesTerminal.Core.History.IHistoryStore>(sp => new AiFuturesTerminal.Core.History.SqliteHistoryStore(historyDbPath));
        // register a real history sync service implementation
        services.AddSingleton<AiFuturesTerminal.Core.History.IHistorySyncService, AiFuturesTerminal.Core.History.HistorySyncService>();


        // register backtest history persister
        services.AddSingleton<Core.Backtest.IBacktestHistoryService, Core.Backtest.BacktestHistoryService>();
        services.AddSingleton<Core.Backtest.BacktestPersistHookRegistrar>();

        // BacktestPersistHookRegistrar handles BacktestEngine.BacktestResultPersistHook registration via DI.
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
