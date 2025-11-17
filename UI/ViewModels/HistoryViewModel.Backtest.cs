using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Backtest;

namespace AiFuturesTerminal.UI.ViewModels;

public sealed partial class HistoryViewModel
{
    // Backtest runs dropdown
    public ObservableCollection<BacktestRunInfo> BacktestRuns { get; } = new();
    public BacktestRunInfo? SelectedBacktestRun { get; set; }

    public ICommand LoadBacktestRunsCommand { get; }
    public ICommand LoadSelectedRunCommand { get; }

    public ITradeBook? SelectedRunTradeBook { get; private set; }

    private readonly Core.Backtest.IBacktestHistoryService? _backtestHistoryService;
    private readonly IHistoryStore? _historyStore;
    private readonly Core.Analytics.TradeAnalyticsService? _analyticsService;

    public HistoryViewModel(IOrderHistoryService orderHistory, ITradeHistoryService tradeHistory, IPositionHistoryService positionHistory, Core.Backtest.IBacktestHistoryService backtestHistoryService, IHistoryStore store, Core.Analytics.TradeAnalyticsService? analyticsService = null)
        : this(orderHistory, tradeHistory, positionHistory)
    {
        _backtestHistoryService = backtestHistoryService;
        _historyStore = store;
        _analyticsService = analyticsService;

        LoadBacktestRunsCommand = new RelayCommand(async _ => await LoadRunsAsync(), _ => true);
        LoadSelectedRunCommand = new RelayCommand(async _ => await LoadSelectedRunAsync(), _ => SelectedBacktestRun != null);
    }

    private async Task LoadRunsAsync()
    {
        BacktestRuns.Clear();
        if (_backtestHistoryService == null) return;
        var runs = await _backtestHistoryService.ListBacktestRunsAsync(100).ConfigureAwait(false);
        App.Current.Dispatcher.Invoke(() => {
            foreach (var r in runs) BacktestRuns.Add(r);
        });
    }

    private async Task LoadSelectedRunAsync()
    {
        if (SelectedBacktestRun is null) return;
        // query trades for this run
        var q = new HistoryQuery { From = DateTimeOffset.MinValue, To = DateTimeOffset.MaxValue, RunId = SelectedBacktestRun.RunId, Page = 1, PageSize = 2000 };
        var ts = await _historyStore!.QueryTradesAsync(q).ConfigureAwait(false);
        Trades.Clear();
        App.Current.Dispatcher.Invoke(() => {
            foreach (var t in ts) Trades.Add(t);
        });

        // build an ITradeBook and update analytics service
        var tb = HistoricalTradeBookFactory.CreateFromHistory(ts, SelectedBacktestRun.RunId);
        SelectedRunTradeBook = tb;
        if (_analyticsService != null)
        {
            _analyticsService.SetTradeBook(tb);
        }
    }
}
