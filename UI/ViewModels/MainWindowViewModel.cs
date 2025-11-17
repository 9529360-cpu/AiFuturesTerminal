using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AiFuturesTerminal.Core.Backtest;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.MarketData;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Orchestration;
using AiFuturesTerminal.Core;
using AiFuturesTerminal.Agent;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Strategy;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Risk;
using AiFuturesTerminal.UI.Views;
using AiFuturesTerminal.Core.Environment;
using System.Windows.Threading;
using AiFuturesTerminal.Core.History;

namespace AiFuturesTerminal.UI.ViewModels;

/// <summary>
/// 主窗口 ViewModel，维护界面状态与命令。
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly BacktestEngine _backtestEngine;
    private readonly MarketDataService _marketDataService;
    private readonly AgentOrchestrator _orchestrator;
    private readonly AppEnvironmentOptions _envOptions;
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly StrategyConfig _strategyConfig;
    private readonly StrategyWatchConfig _watchConfig;
    private readonly StrategyWatchConfigService _watchConfigService;
    private readonly ExecutionEngine _executionEngine;
    private readonly ITradeBook _tradeBook;
    private readonly IBacktestService _backtestService;
    private readonly Func<BacktestWindow> _backtestWindowFactory;
    private readonly Func<BacktestViewModel> _backtestViewModelFactory;
    private readonly BinanceTradeViewService? _binanceTradeViewService;
    private readonly IBinanceState? _binanceState;

    // newly injected
    private readonly ITradingEnvironmentFactory _envFactory;
    private readonly IAgentService _agentService;
    private readonly Func<UI.Views.AnalyticsWindow> _analyticsWindowFactory;
    private readonly BinanceTradeSyncService? _binanceSync;
    public ICommand OpenAnalyticsCommand { get; } = null!;

    private CancellationTokenSource? _agentLoopCts;
    private CancellationTokenSource? _binanceRefreshCts;

    private string _statusText = "AI 交易终端准备就绪";

    /// <summary>状态行文字显示用</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>环境显示文字</summary>
    public string EnvironmentDisplay { get; private set; } = string.Empty;

    /// <summary>执行模式显示</summary>
    public string ExecutionModeDisplay =>
        _envOptions.ExecutionMode switch
        {
            ExecutionMode.DryRun => "执行模式：DryRun（仅记录，不下单）",
            ExecutionMode.Testnet => "执行模式：Testnet（真实下单到测试网）",
            _ => $"执行模式：{_envOptions.ExecutionMode}"
        };

    public ObservableCollection<string> Logs { get; } = new();

    // Commands - initialize with null! to satisfy nullable checks; constructor assigns real instances
    public ICommand RunBacktestCommand { get; } = null!;
    public ICommand RunAgentOnceCommand { get; } = null!;
    public ICommand BinanceDiagnosticsCommand { get; } = null!;
    public ICommand StartAgentLoopCommand { get; } = null!;
    public ICommand StopAgentLoopCommand { get; } = null!;
    public ICommand SaveWatchConfigCommand { get; } = null!;
    public ICommand OpenTradeBookCommand { get; } = null!;
    public ICommand OpenBacktestCommand { get; } = null!;
    public ICommand RefreshPositionsCommand { get; } = null!;
    public ICommand RefreshBinanceHistoryCommand { get; } = null!;
    public ICommand? ClearHistoryDbCommand { get; private set; }

    // new: expose Binance-backed current positions using PositionDto
    public ObservableCollection<AiFuturesTerminal.Core.Exchanges.PositionDto> CurrentPositions { get; } = new();
    public ObservableCollection<BinanceOrderRow> BinanceHistoryOrders { get; } = new();

    // helper to determine if we should show Binance cached data
    private bool IsBinanceMode => SelectedExecutionMode == ExecutionMode.Testnet || SelectedExecutionMode == ExecutionMode.Live;

    // hint property for UI when no positions
    public bool ShowNoPositionsHint => IsBinanceMode && CurrentPositions.Count == 0;

    public event EventHandler? OpenTradeBookRequested;

    // Strategy selection
    public IList<StrategyKind> AvailableStrategyKinds { get; } = Enum.GetValues<StrategyKind>().Cast<StrategyKind>().ToList();

    public StrategyKind SelectedStrategyKind
    {
        get => _strategyConfig.Kind;
        set
        {
            if (_strategyConfig.Kind == value) return;
            _strategyConfig.Kind = value;
            OnPropertyChanged();

            // apply the selected strategy to all monitored symbol rows
            App.Current.Dispatcher.Invoke(() =>
            {
                foreach (var row in SymbolStatuses)
                {
                    row.Strategy = value;
                }

                var msg = $"已将当前策略切换为 {value}，并同步到所有监控行。";
                Logs.Add(msg);
                StatusText = msg;
            });
        }
    }

    // Symbol statuses
    private readonly Dictionary<string, SymbolStatus> _symbolStatusLookup = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<SymbolStatus> SymbolStatuses { get; } = new();

    // available execution modes
    public IReadOnlyList<ExecutionMode> AvailableExecutionModes { get; } = new[]
    {
        ExecutionMode.DryRun,
        ExecutionMode.Testnet,
    };

    private ExecutionMode _selectedExecutionMode;
    public ExecutionMode SelectedExecutionMode
    {
        get => _selectedExecutionMode;
        set
        {
            if (_selectedExecutionMode == value) return;

            // update internal field first
            _selectedExecutionMode = value;
            _envOptions.ExecutionMode = value;

            // notify selection and display
            OnPropertyChanged(nameof(SelectedExecutionMode));
            OnPropertyChanged(nameof(ExecutionModeDisplay));

            CommandManager.InvalidateRequerySuggested();

            var msg = $"已切换执行模式为 {ExecutionModeDisplay}";
            App.Current.Dispatcher.Invoke(() =>
            {
                Logs.Add(msg);
                StatusText = msg;
            });

            // update tradebook write status and notify
            UpdateTradeBookWriteStatus();
            OnPropertyChanged(nameof(TradeBookWriteStatus));

            // Notify any open TradeBookWindow instances to update their SourceText
            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (Window w in App.Current.Windows)
                    {
                        if (w is AiFuturesTerminal.UI.Views.TradeBookWindow tbw && tbw.DataContext is TradeBookViewModel tbvm)
                        {
                            tbvm.UpdateSourceText(_selectedExecutionMode);
                        }
                    }
                });
            }
            catch { }

            // when switching to Testnet/Live refresh Binance panels
            _ = Task.Run(() => RefreshBinancePanelsAsync());

            // refresh current positions immediately
            RefreshCurrentPositionsFromState();
            OnPropertyChanged(nameof(ShowNoPositionsHint));

            // refresh trades and summary for new mode
            _ = Task.Run(async () => { await RefreshTodaySummaryAsync().ConfigureAwait(false); });

            // start/stop automatic refresh based on mode
            if (_selectedExecutionMode == ExecutionMode.Testnet || _selectedExecutionMode == ExecutionMode.Live)
            {
                StartBinanceAutoRefresh();
            }
            else
            {
                StopBinanceAutoRefresh();
            }
        }
    }

    public GlobalRiskStatusViewModel GlobalRiskStatus { get; }

    private readonly Core.Analytics.ProtectedTradeBook? _protectedTradeBook;

    private string _tradeBookWriteStatus = string.Empty;
    public string TradeBookWriteStatus
    {
        get => _tradeBookWriteStatus;
        set => SetProperty(ref _tradeBookWriteStatus, value);
    }

    public HistoryViewModel History { get; }

    // expose TodayHistory VM for direct binding and forwarding
    public TodayHistoryViewModel TodayHistory => _todayHistoryViewModel;

    // expose shortcuts for UI binding (history)
    public ObservableCollection<OrderHistoryRecord> TodayHistoryOrders => _todayHistoryViewModel.TodayOrders;
    public ObservableCollection<TradeHistoryRecord> TodayHistoryTrades => _todayHistoryViewModel.TodayTrades;
    public ICommand RefreshTodayHistoryCommand => _todayHistoryViewModel.RefreshTodayHistoryCommand;

    // add property field
    private DateTimeOffset? _lastPositionsRefreshTime;

    public DateTimeOffset? LastPositionsRefreshTime
    {
        get => _lastPositionsRefreshTime;
        private set
        {
            if (SetProperty(ref _lastPositionsRefreshTime, value))
            {
                // notify derived local time property
                OnPropertyChanged(nameof(LastPositionsRefreshLocalTime));
            }
        }
    }

    // new read-only local time property
    public DateTime? LastPositionsRefreshLocalTime => LastPositionsRefreshTime?.ToLocalTime().DateTime;

    private readonly TodayHistoryViewModel _todayHistoryViewModel;

    // risk service
    private readonly IRiskStatusService _riskStatusService;
    private readonly DispatcherTimer _riskRefreshTimer;

    // agent run logs
    private readonly AiFuturesTerminal.Core.Orchestration.IAgentRunLogSink _runLogSink;
    public ObservableCollection<AiFuturesTerminal.Core.Orchestration.AgentRunLog> AgentRunLogs { get; } = new();

    private GlobalRiskStatus? _riskStatus;
    public GlobalRiskStatus? RiskStatus
    {
        get => _riskStatus;
        private set
        {
            if (EqualityComparer<GlobalRiskStatus?>.Default.Equals(_riskStatus, value)) return;
            _riskStatus = value;
            OnPropertyChanged(nameof(RiskStatus));
            OnPropertyChanged(nameof(RiskTodayRealizedPnl));
            OnPropertyChanged(nameof(RiskTodayMaxDrawdown));
            OnPropertyChanged(nameof(RiskConsecutiveLosingTrades));
            OnPropertyChanged(nameof(RiskIsOpenNewPositionsFrozen));
            OnPropertyChanged(nameof(RiskStateDisplay));
        }
    }

    // derived properties for binding
    public decimal RiskTodayRealizedPnl => RiskStatus?.TodayRealizedPnl ?? 0m;
    public decimal RiskTodayMaxDrawdown => RiskStatus?.TodayMaxDrawdown ?? 0m;
    public int RiskConsecutiveLosingTrades => RiskStatus?.ConsecutiveLosingTrades ?? 0;
    public bool RiskIsOpenNewPositionsFrozen => RiskStatus?.IsOpenNewPositionsFrozen ?? false;
    public string RiskStateDisplay => RiskIsOpenNewPositionsFrozen ? "冻结（禁止新开仓）" : "正常";

    public MainWindowViewModel(BacktestEngine backtestEngine,
        MarketDataService marketDataService,
        AgentOrchestrator orchestrator,
        AppEnvironmentOptions envOptions,
        StrategyConfig strategyConfig,
        StrategyWatchConfig watchConfig,
        StrategyWatchConfigService watchConfigService,
        ExecutionEngine executionEngine,
        IExchangeAdapter exchangeAdapter,
        ITradeBook tradeBook,
        GlobalRiskCoordinator riskCoordinator,
        IBacktestService backtestService,
        Func<BacktestWindow> backtestWindowFactory,
        Func<BacktestViewModel> backtestViewModelFactory,
        ITradingEnvironmentFactory envFactory,
        IAgentService agentService,
        Func<UI.Views.AnalyticsWindow> analyticsWindowFactory,
        HistoryViewModel historyViewModel,
        TodayHistoryViewModel todayHistoryViewModel,
        IRiskStatusService riskStatusService,
        BinanceTradeSyncService? binanceSync = null,
        BinanceTradeViewService? binanceTradeViewService = null,
        IBinanceState? binanceState = null,
        AiFuturesTerminal.Core.Orchestration.IAgentRunLogSink? runLogSink = null)
    {
        _backtestEngine = backtestEngine ?? throw new ArgumentNullException(nameof(backtestEngine));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
        _exchangeAdapter = exchangeAdapter ?? throw new ArgumentNullException(nameof(exchangeAdapter));
        _strategyConfig = strategyConfig ?? throw new ArgumentNullException(nameof(strategyConfig));
        _watchConfig = watchConfig ?? throw new ArgumentNullException(nameof(watchConfig));
        _watchConfigService = watchConfigService ?? throw new ArgumentNullException(nameof(watchConfigService));
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
        _backtestService = backtestService ?? throw new ArgumentNullException(nameof(backtestService));
        _protectedTradeBook = _tradeBook as Core.Analytics.ProtectedTradeBook;
        _binanceTradeViewService = binanceTradeViewService;
        _binanceState = binanceState;

        if (_binanceState != null)
        {
            _binanceState.PositionsChanged += OnBinancePositionsChanged;
            // initialize from snapshot
            RefreshCurrentPositionsFromState();
        }

        // ensure SelectedExecutionMode matches environment options at startup
        try
        {
            SelectedExecutionMode = _envOptions.ExecutionMode;
        }
        catch { }

        // set initial tradebook write status
        UpdateTradeBookWriteStatus();

        // subscribe to backtest service log events to centralize logs in main window
        try
        {
            _backtestService.Log += msg => App.Current.Dispatcher.Invoke(() => Logs.Add(msg));
        }
        catch { }
        _backtestWindowFactory = backtestWindowFactory ?? throw new ArgumentNullException(nameof(backtestWindowFactory));
        _backtestViewModelFactory = backtestViewModelFactory ?? throw new ArgumentNullException(nameof(backtestViewModelFactory));
        _envFactory = envFactory ?? throw new ArgumentNullException(nameof(envFactory));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _analyticsWindowFactory = analyticsWindowFactory ?? throw new ArgumentNullException(nameof(analyticsWindowFactory));
        _binanceSync = binanceSync;
        _runLogSink = runLogSink ?? throw new ArgumentNullException(nameof(runLogSink));

        // assign history VM
        History = historyViewModel ?? throw new ArgumentNullException(nameof(historyViewModel));
        _todayHistoryViewModel = todayHistoryViewModel ?? throw new ArgumentNullException(nameof(todayHistoryViewModel));

        _binanceTradeViewService = binanceTradeViewService;
        _binanceState = binanceState;

        // risk status service
        _riskStatusService = riskStatusService ?? throw new ArgumentNullException(nameof(riskStatusService));

        // timer to refresh risk status periodically
        _riskRefreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, __) => RefreshRiskStatus(), App.Current.Dispatcher);
        _riskRefreshTimer.Start();

        // initialize risk status VM using injected coordinator and forward logs
        GlobalRiskStatus = new GlobalRiskStatusViewModel(riskCoordinator, _strategyConfig, msg => App.Current.Dispatcher.Invoke(() => Logs.Add(msg)));

        // subscribe structured execution info for updating risk status
        _executionEngine.ExecutionInfoStructured += (_, __) => GlobalRiskStatus.Refresh();

        OpenAnalyticsCommand = new RelayCommand(_ => OpenAnalytics(), _ => true);

        // Initialize other UI commands so buttons actually invoke logic
        RunAgentOnceCommand = new RelayCommand(async _ => await RunAgentOnceAsync(), _ => true);
        StartAgentLoopCommand = new RelayCommand(async _ => await StartAgentLoopAsync(), CanStartAgentLoop);
        StopAgentLoopCommand = new RelayCommand(async _ => await StopAgentLoopAsync(), CanStopAgentLoop);
        RunBacktestCommand = new RelayCommand(async _ => await RunBacktestAsync(), _ => true);
        BinanceDiagnosticsCommand = new RelayCommand(async _ => await RunBinanceDiagnosticsAsync(), _ => true);
        OpenBacktestCommand = new RelayCommand(_ => OpenBacktest(), _ => true);
        SaveWatchConfigCommand = new RelayCommand(_ => SaveWatchConfig(), _ => true);
        OpenTradeBookCommand = new RelayCommand(_ => OnOpenTradeBookRequested(), _ => true);
        RefreshPositionsCommand = new RelayCommand(_ => RefreshCurrentPositionsFromState(), _ => true);
        RefreshBinanceHistoryCommand = new RelayCommand(async _ => await RefreshBinancePanelsAsync(), _ => true);

#if DEBUG
        ClearHistoryDbCommand = new RelayCommand(_ => {
            try
            {
                var confirm = MessageBox.Show("确认删除本地 history.db？此操作只影响本地历史记录。", "确认清空历史库", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                {
                    var historyDbPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "data", "history.db");
                    SqliteHistoryStore.ClearDatabaseForDevelopment(historyDbPath);
                    Logs.Add("History DB cleared");

                    // refresh today trades
                    _ = Task.Run(async () => {
                        try
                        {
                            await _todayHistoryViewModel.RefreshTodayHistoryAsync().ConfigureAwait(false);
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                try { Logs.Add("ClearHistoryDb failed: " + ex.Message); } catch { }
            }
        }, _ => true);
#endif

        // initialize Binance panels on construction if Testnet/Live
        _ = Task.Run(() => RefreshBinancePanelsAsync());
        if (_selectedExecutionMode == ExecutionMode.Testnet || _selectedExecutionMode == ExecutionMode.Live)
        {
            StartBinanceAutoRefresh();
        }

        // initial load of today's history/summary
        _ = Task.Run(async () => {
            await _todayHistoryViewModel.RefreshTodayHistoryAsync().ConfigureAwait(false);
            await RefreshTodaySummaryAsync().ConfigureAwait(false);
            // initial risk status load
            RefreshRiskStatus();
        });

        // timer to refresh agent logs
        var logTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, __) => RefreshAgentLogs(), App.Current.Dispatcher);
        logTimer.Start();
    }

    private void SaveWatchConfig()
    {
        try
        {
            _watchConfigService.Save(_watchConfig);
            StatusText = "监控配置已保存";
            Logs.Add("已保存监控配置到 JSON 文件");
        }
        catch (Exception ex)
        {
            StatusText = "保存监控配置失败";
            Logs.Add($"保存监控配置失败：{ex.Message}");
        }
    }

    private void OnAgentDecisionProduced(AiFuturesTerminal.Agent.AgentDecision decision)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add($"Agent 决策：{decision.StrategyName} | 说明:{decision.Explanation} | Confidence: {decision.Confidence}");

            if (string.IsNullOrWhiteSpace(decision.ExecutionDecision.Symbol))
                return;

            if (!_symbolStatusLookup.TryGetValue(decision.ExecutionDecision.Symbol, out var row))
                return;

            row.LastAction = decision.ExecutionDecision.Type.ToString();
            row.LastExplanation = decision.Explanation ?? string.Empty;
            row.LastUpdated = DateTime.Now;

            switch (decision.ExecutionDecision.Type)
            {
                case ExecutionDecisionType.OpenLong:
                    row.PositionText = "准备/看多";
                    break;
                case ExecutionDecisionType.OpenShort:
                    row.PositionText = "准备/看空";
                    break;
                case ExecutionDecisionType.Close:
                    row.PositionText = "平仓";
                    break;
            }
        });
    }

    private void OnDryRunPlanned(object? sender, string message)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(message);
            StatusText = "收到一条 DryRun 执行计划";
        });
    }

    private void OnExecutionInfo(object? sender, string message)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(message);
            StatusText = message;
        });
    }

    private void OnExecutionInfoStructured(object? sender, ExecutionInfo info)
    {
        if (info == null) return;

        App.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add($"[Exec:{info.Kind}] {info.Symbol}: {info.Message}");
            StatusText = info.Message;

            // handle risk blocked display
            if (info.Kind == ExecutionInfoKind.RiskBlocked)
            {
                if (_symbolStatusLookup.TryGetValue(info.Symbol, out var row))
                {
                    row.RiskNote = info.Message;
                    row.LastAction = "风控拦截";
                    row.LastUpdated = info.Time;
                }
            }

            // clear risk note on successful order/place/close
            if (info.Kind == ExecutionInfoKind.OrderPlaced || info.Kind == ExecutionInfoKind.OrderClosed)
            {
                if (_symbolStatusLookup.TryGetValue(info.Symbol, out var row))
                {
                    row.RiskNote = null;
                    row.LastAction = info.Kind == ExecutionInfoKind.OrderPlaced ? "已下单" : "已平仓";
                    row.LastUpdated = info.Time;
                }
            }
        });
    }

    private void OnTradeRecorded(object? sender, TradeRecord e)
    {
        _ = Task.Run(() => RefreshTodaySummaryAsync());
    }

    private async Task RefreshTodaySummaryAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // If running against Testnet/Live, prefer BinanceTradeViewService as authoritative
            if ((_envOptions.ExecutionMode == ExecutionMode.Testnet || _envOptions.ExecutionMode == ExecutionMode.Live) && _binanceTradeViewService != null)
            {
                var summary = await _binanceTradeViewService.GetTodaySummaryAsync(null, CancellationToken.None).ConfigureAwait(false);
                App.Current.Dispatcher.Invoke(() => {
                    TodayTradeCount = summary.Trades;
                    TodayTotalPnl = summary.NetPnl;
                    TodayWinRate = (decimal)summary.WinRate;
                    TodayMaxDrawdown = 0m; // extend if service provides
                    StatusText = $"今日成交 {TodayTradeCount} 笔，盈亏 {TodayTotalPnl:F2} USDT，胜率 {(TodayWinRate * 100m):F1}%";
                });
                return;
            }

            // fallback to local tradebook
            var todaySummary = _tradeBook.GetDailySummary(today);
            App.Current.Dispatcher.Invoke(() => {
                TodayTradeCount = todaySummary.TradeCount;
                TodayTotalPnl = todaySummary.TotalPnL;
                TodayWinRate = todaySummary.WinRate;
                TodayMaxDrawdown = todaySummary.MaxDrawdown;
                StatusText = $"今日成交 {TodayTradeCount} 笔，盈亏 {TodayTotalPnl:F2} USDT，胜率 {(TodayWinRate * 100m):F1}%";
            });
        }
        catch (Exception ex)
        {
            try { App.Current.Dispatcher.Invoke(() => Logs.Add($"RefreshTodaySummary error: {ex.Message}")); } catch { }
        }
    }

    private int _todayTradeCount;
    public int TodayTradeCount
    {
        get => _todayTradeCount;
        set => SetProperty(ref _todayTradeCount, value);
    }

    private decimal _todayTotalPnl;
    public decimal TodayTotalPnl
    {
        get => _todayTotalPnl;
        set => SetProperty(ref _todayTotalPnl, value);
    }

    private decimal _todayWinRate;
    public decimal TodayWinRate
    {
        get => _todayWinRate;
        set => SetProperty(ref _todayWinRate, value);
    }

    private decimal _todayMaxDrawdown;
    public decimal TodayMaxDrawdown
    {
        get => _todayMaxDrawdown;
        set => SetProperty(ref _todayMaxDrawdown, value);
    }

    private async Task StartAgentLoopAsync()
    {
        if (_agentLoopCts != null)
            return; // already running

        _agentLoopCts = new CancellationTokenSource();
        var token = _agentLoopCts.Token;
        var mode = SelectedExecutionMode;
        var interval = TimeSpan.FromMinutes(1);

        App.Current.Dispatcher.Invoke(() => Logs.Add($"启动智能体循环，模式={mode}, 间隔={interval}"));
        StatusText = "智能体循环已启动...";

        // run orchestrator loop in background so UI thread not blocked
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.RunLoopAsync(mode, interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancel
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => Logs.Add("Agent loop 异常：" + ex.Message));
            }
            finally
            {
                // ensure CTS disposed and UI updated
                try { _agentLoopCts?.Dispose(); } catch { }
                _agentLoopCts = null;
                App.Current.Dispatcher.Invoke(() => {
                    Logs.Add("智能体循环已退出");
                    StatusText = "智能体循环已停止";
                });
                CommandManager.InvalidateRequerySuggested();
            }
        });

        CommandManager.InvalidateRequerySuggested();
    }

    private async Task StopAgentLoopAsync()
    {
        if (_agentLoopCts == null)
            return;

        App.Current.Dispatcher.Invoke(() => Logs.Add("停止智能体循环..."));
        _agentLoopCts.Cancel();

        // also call orchestrator stop if it manages an internal loop
        try
        {
            await _orchestrator.StopLoopAsync().ConfigureAwait(false);
        }
        catch { }

        // UI will be updated by background finally block
    }

    private async Task RunAgentOnceAsync()
    {
        StatusText = "正在执行一次智能体决策...";
        App.Current.Dispatcher.Invoke(() => Logs.Add($"开始运行一次 Agent 决策（模式={SelectedExecutionMode}）..."));

        try
        {
            var env = _envFactory.Create(SelectedExecutionMode);
            await _agentService.RunOnceAsync(env, CancellationToken.None).ConfigureAwait(false);

            App.Current.Dispatcher.Invoke(() => Logs.Add($"[Agent] RunOnce completed in mode {SelectedExecutionMode}"));
            StatusText = "智能体单次执行完成";
        }
        catch (Exception ex)
        {
            StatusText = "智能体执行失败";
            App.Current.Dispatcher.Invoke(() => Logs.Add("执行异常：" + ex.Message));
        }
    }

    private async Task RunBacktestAsync()
    {
        StatusText = "回测中...";
        Logs.Clear();

        try
        {
            var request = new Core.Backtest.BacktestRequest("BTCUSDT", _strategyConfig.Kind, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, _strategyConfig);
            var res = await _backtestService.RunAsync(request, CancellationToken.None).ConfigureAwait(false);

            App.Current.Dispatcher.Invoke(() => Logs.Add($"回测初始权益: {1000m}"));
            App.Current.Dispatcher.Invoke(() => Logs.Add($"回测最终权益: {res.Summary.NetPnl + 1000m}"));
            App.Current.Dispatcher.Invoke(() => Logs.Add($"回测成交笔数: {res.Summary.TradeCount}"));

            StatusText = $"回测完成 {1000m} -> {res.Summary.NetPnl + 1000m} 共 {res.Summary.TradeCount} 笔";
        }
        catch (Exception ex)
        {
            StatusText = "回测失败";
            App.Current.Dispatcher.Invoke(() => Logs.Add("回测异常: " + ex.Message));
        }
    }

    private void OnOpenTradeBookRequested()
    {
        OpenTradeBookRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenBacktest()
    {
        var vm = _backtestViewModelFactory();
        vm.Logger = msg => App.Current.Dispatcher.Invoke(() => Logs.Add(msg));
        var window = _backtestWindowFactory();
        window.DataContext = vm;
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }

    private void OpenAnalytics()
    {
        try
        {
            var win = _analyticsWindowFactory();
            win.Owner = Application.Current.MainWindow;
            win.Show();
        }
        catch (Exception ex)
        {
            App.Current.Dispatcher.Invoke(() => Logs.Add("打开分析窗口失败: " + ex.Message));
        }
    }

    private async Task RunBinanceDiagnosticsAsync()
    {
        App.Current.Dispatcher.Invoke(() => Logs.Add("开始执行交易所连通性与数据自检..."));
        StatusText = "正在检测交易所...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ct = cts.Token;

            var candles = await _exchangeAdapter.GetHistoricalCandlesAsync("BTCUSDT", TimeSpan.FromMinutes(1), 50, ct);
            Candle? latest = candles.Count > 0 ? candles.Last() : (Candle?)null;
            App.Current.Dispatcher.Invoke(() => Logs.Add($"[K线] 返回条数={candles.Count}, 最近时间={latest?.CloseTime}, 最近收盘={latest?.Close}"));

            var account = await _exchangeAdapter.GetAccountSnapshotAsync(ct);
            App.Current.Dispatcher.Invoke(() => Logs.Add($"[账户] Equity={account.Equity}, FreeBalance={account.FreeBalance}, 时间={account.Timestamp}"));

            var position = await _exchangeAdapter.GetOpenPositionAsync("BTCUSDT", ct);
            if (position is null || position.IsFlat())
                App.Current.Dispatcher.Invoke(() => Logs.Add("[持仓] BTCUSDT 当前为 Flat"));
            else
                App.Current.Dispatcher.Invoke(() => Logs.Add($"[持仓] Symbol={position.Symbol}, Side={position.Side}, Qty={position.Quantity}, EntryPrice={position.EntryPrice}"));

            StatusText = "交易所检测完成";
        }
        catch (Exception ex)
        {
            App.Current.Dispatcher.Invoke(() => Logs.Add($"[Diagnostics] {ex.GetType().Name}: {ex.Message}"));
            StatusText = "交易所检测失败，请查看日志";
        }
    }

    private async Task RefreshBinancePanelsAsync()
    {
        try
        {
            // clear current collections
            if (_envOptions.ExecutionMode != ExecutionMode.Testnet && _envOptions.ExecutionMode != ExecutionMode.Live)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    CurrentPositions.Clear();
                    BinanceHistoryOrders.Clear();
                });
                return;
            }

            // Ensure we clear BinanceHistoryOrders before repopulating to avoid duplicates
            App.Current.Dispatcher.Invoke(() => BinanceHistoryOrders.Clear());

            // use BinanceTradeViewService if available, otherwise fallback to IBinanceState
            if (_binanceTradeViewService != null)
            {
                var trades = await _binanceTradeViewService.GetTodayTradeRecordsAsync(null, CancellationToken.None).ConfigureAwait(false);
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var t in trades.Reverse())
                    {
                        BinanceHistoryOrders.Add(new BinanceOrderRow
                        {
                            Time = t.CloseTime,
                            Symbol = t.Symbol,
                            Side = t.Side.ToString(),
                            Quantity = t.Quantity,
                            EntryPrice = t.EntryPrice,
                            ExitPrice = t.ExitPrice,
                            RealizedPnl = t.RealizedPnl,
                            Fee = t.Fee
                        });
                    }
                });
            }
            else if (_binanceState != null)
            {
                var now = DateTime.UtcNow;
                var trades = await _binanceState.GetRecentTradesAsync(now.Date, now.Date.AddDays(1).AddSeconds(-1), null, CancellationToken.None).ConfigureAwait(false);
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var t in trades.Reverse())
                    {
                        BinanceHistoryOrders.Add(new BinanceOrderRow
                        {
                            Time = t.CloseTime,
                            Symbol = t.Symbol,
                            Side = t.Side.ToString(),
                            Quantity = t.Quantity,
                            EntryPrice = t.EntryPrice,
                            ExitPrice = t.ExitPrice,
                            RealizedPnl = t.RealizedPnl,
                            Fee = t.Fee
                        });
                    }
                });
            }

            // refresh current positions only in Testnet/Live
            if (_envOptions.ExecutionMode == ExecutionMode.Testnet || _envOptions.ExecutionMode == ExecutionMode.Live)
            {
                // use snapshot from state service to update CurrentPositions
                RefreshCurrentPositionsFromState();
            }

            // refresh today's trades as well
            _ = Task.Run(async () => await _todayHistoryViewModel.RefreshTodayHistoryAsync());
        }
        catch (Exception ex)
        {
            App.Current.Dispatcher.Invoke(() => Logs.Add($"刷新 Binance 面板失败: {ex.Message}"));
        }
    }

    public void Dispose()
    {
        try { _agentLoopCts?.Cancel(); } catch { }
        try { _agentLoopCts?.Dispose(); } catch { }
        try { _binanceRefreshCts?.Cancel(); } catch { }
        try { _binanceRefreshCts?.Dispose(); } catch { }
        try { if (_binanceState != null) _binanceState.PositionsChanged -= OnBinancePositionsChanged; } catch { }
    }

    // simple DTOs for UI rows
    public sealed class BinancePositionRow
    {
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal UnrealizedPnl { get; set; }
        public decimal Leverage { get; set; }
        public DateTime? EntryTime { get; set; }
    }

    public sealed class BinanceOrderRow
    {
        public DateTime Time { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal Fee { get; set; }
        public string Mode { get; set; } = string.Empty; // Testnet / Live
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // add OnBinancePositionsChanged implementation
    private void OnBinancePositionsChanged(object? sender, EventArgs e)
    {
        if (!IsBinanceMode) return;
        RefreshCurrentPositionsFromState();
        OnPropertyChanged(nameof(ShowNoPositionsHint));

        // update last refresh time
        LastPositionsRefreshTime = DateTimeOffset.UtcNow;
    }

    private bool CanStartAgentLoop(object? _) => _agentLoopCts == null;
    private bool CanStopAgentLoop(object? _) => _agentLoopCts != null;

    private void StopAgentLoop()
    {
        _agentLoopCts?.Cancel();
    }

    private void StartBinanceAutoRefresh()
    {
        // already running
        if (_binanceRefreshCts != null) return;
        _binanceRefreshCts = new CancellationTokenSource();
        var ct = _binanceRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await RefreshBinancePanelsAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                try { App.Current.Dispatcher.Invoke(() => Logs.Add("Binance 自动刷新异常: " + ex.Message)); } catch { }
            }
        }, ct);
    }

    private void StopBinanceAutoRefresh()
    {
        try { _binanceRefreshCts?.Cancel(); } catch { }
        try { _binanceRefreshCts?.Dispose(); } catch { }
        _binanceRefreshCts = null;
    }

    private void UpdateTradeBookWriteStatus()
    {
        if (_tradeBook is Core.Analytics.ProtectedTradeBook p)
        {
            TradeBookWriteStatus = p.IsWriteEnabled
                ? "TradeBook 状态：可写（回测/模拟模式）"
                : "TradeBook 状态：只读（Testnet/实盘模式，本地不记录真实成交）";
        }
        else
        {
            TradeBookWriteStatus = "TradeBook 状态：未知";
        }
    }

    private void RefreshCurrentPositionsFromState()
    {
        if (!IsBinanceMode || _binanceState == null)
        {
            Application.Current.Dispatcher.Invoke(() => {
                CurrentPositions.Clear();
                OnPropertyChanged(nameof(ShowNoPositionsHint));
            });
            return;
        }

        try
        {
            var snapshot = _binanceState.GetOpenPositionsSnapshot() ?? Array.Empty<AiFuturesTerminal.Core.Exchanges.PositionDto>();
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentPositions.Clear();
                foreach (var p in snapshot)
                {
                    if (p.Quantity == 0) continue;
                    CurrentPositions.Add(p);
                }
                OnPropertyChanged(nameof(ShowNoPositionsHint));

                // update last refresh time after sync
                LastPositionsRefreshTime = DateTimeOffset.UtcNow;
            });
        }
        catch (Exception ex)
        {
            try { Application.Current.Dispatcher.Invoke(() => Logs.Add($"刷新当前仓位失败: {ex.Message}")); } catch { }
        }
    }

    private void RefreshRiskStatus()
    {
        try
        {
            var s = _riskStatusService.GetCurrentStatus();
            App.Current.Dispatcher.Invoke(() => RiskStatus = s);
        }
        catch (Exception ex)
        {
            try { App.Current.Dispatcher.Invoke(() => Logs.Add("刷新风险状态失败: " + ex.Message)); } catch { }
        }
    }

    private void RefreshAgentLogs()
    {
        try
        {
            var snaps = _runLogSink.Snapshot(200);
            App.Current.Dispatcher.Invoke(() =>
            {
                AgentRunLogs.Clear();
                foreach (var r in snaps)
                    AgentRunLogs.Add(r);
            });
        }
        catch { }
    }
}
/// <summary>
/// 简单 RelayCommand 实现，用于绑定命令
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
