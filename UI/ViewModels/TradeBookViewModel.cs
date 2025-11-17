using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.UI.ViewModels;

public sealed class TradeBookViewModel : INotifyPropertyChanged
{
    private readonly ITradeBook _tradeBook;
    private readonly Core.Analytics.BinanceTradeViewService? _binanceView;
    private readonly AppEnvironmentOptions _envOptions;

    public ObservableCollection<TradeRecordRow> Trades { get; } = new();

    public ICommand RefreshCommand { get; }

    private string _sourceText = string.Empty;
    public string SourceText { get => _sourceText; set { _sourceText = value; OnPropertyChanged(); } }

    private string _lastEventText = string.Empty;
    public string LastEventText { get => _lastEventText; set { _lastEventText = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TradeBookViewModel(ITradeBook tradeBook, Core.Analytics.BinanceTradeViewService? binanceView = null, AppEnvironmentOptions? envOptions = null, Core.Analytics.ExchangeFillProcessor? fillProcessor = null)
    {
        _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
        _binanceView = binanceView;
        _envOptions = envOptions ?? new AppEnvironmentOptions();

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => true);

        // subscribe to fill events to show suggestion to refresh
        if (fillProcessor != null)
        {
            fillProcessor.TradesUpdated += (s, sym) =>
            {
                // only surface this notification in Testnet/Live modes
                if (_envOptions.ExecutionMode == Core.Execution.ExecutionMode.Testnet || _envOptions.ExecutionMode == Core.Execution.ExecutionMode.Live)
                {
                    try
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            LastEventText = $"[{DateTime.Now:HH:mm:ss}] 收到币安成交：{sym}，如需查看最新列表，请点击上方\"刷新\"。";
                        });
                    }
                    catch
                    {
                        // ignore dispatcher errors; best-effort UI hint only
                    }
                }
            };
        }

        // initialize depending on execution mode
        if (_envOptions.ExecutionMode == Core.Execution.ExecutionMode.Testnet || _envOptions.ExecutionMode == Core.Execution.ExecutionMode.Live)
        {
            SourceText = _envOptions.ExecutionMode == Core.Execution.ExecutionMode.Live ? "数据来源：币安实盘账户。点击刷新从币安获取最新成交。" : "数据来源：币安测试网账户。点击刷新从币安获取最新成交。";
            // load from Binance state asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var trades = await (_binanceView?.GetTodayTradesAsync(null, CancellationToken.None) ?? Task.FromResult((IReadOnlyList<UiTodayTradeRow>)Array.Empty<UiTodayTradeRow>())).ConfigureAwait(false);
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var t in trades) Trades.Add(new TradeRecordRow { Time = t.Time, Symbol = t.Symbol, Side = t.Side, Quantity = t.Quantity, EntryPrice = t.EntryPrice, ExitPrice = t.ExitPrice, Pnl = t.RealizedPnl });
                    });
                }
                catch { }
            });
        }
        else
        {
            // Backtest/DryRun: use local tradebook
            SourceText = "数据来源：本地 TradeBook（回测/仿真）。";
            foreach (var t in _tradeBook.GetAllTrades())
            {
                Trades.Add(TradeRecordRow.FromCore(t));
            }

            // subscribe to new trades
            _tradeBook.TradeRecorded += OnTradeRecorded;
        }
    }

    private void OnTradeRecorded(object? sender, TradeRecord e)
    {
        // ensure UI thread if needed (ViewModel created on UI thread by DI)
        App.Current.Dispatcher.Invoke(() => Trades.Add(TradeRecordRow.FromCore(e)));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // public method to allow external callers (e.g., MainWindowViewModel) to update SourceText when execution mode changes
    public void UpdateSourceText(ExecutionMode mode)
    {
        if (mode == ExecutionMode.Backtest || mode == ExecutionMode.DryRun)
            SourceText = "数据来源：本地回测 / 模拟 TradeBook";
        else if (mode == ExecutionMode.Testnet)
            SourceText = "数据来源：币安 USDT-M 测试网（通过 IBinanceState 实时镜像）";
        else if (mode == ExecutionMode.Live)
            SourceText = "数据来源：币安 USDT-M 实盘账户（通过 IBinanceState 实时镜像）";

        // ensure property changed is fired
        OnPropertyChanged(nameof(SourceText));
    }

    private async Task RefreshAsync()
    {
        try
        {
            Trades.Clear();

            // Recalculate SourceText on each refresh to ensure it's aligned with current mode
            UpdateSourceText(_envOptions.ExecutionMode);

            if (_envOptions.ExecutionMode == Core.Execution.ExecutionMode.Testnet || _envOptions.ExecutionMode == Core.Execution.ExecutionMode.Live)
            {
                var trades = await (_binanceView?.GetTodayTradesAsync(null, CancellationToken.None) ?? Task.FromResult((IReadOnlyList<UiTodayTradeRow>)Array.Empty<UiTodayTradeRow>())).ConfigureAwait(false);
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var t in trades) Trades.Add(new TradeRecordRow { Time = t.Time, Symbol = t.Symbol, Side = t.Side, Quantity = t.Quantity, EntryPrice = t.EntryPrice, ExitPrice = t.ExitPrice, Pnl = t.RealizedPnl });
                });
                LastEventText = "已从币安获取最新成交。";
            }
            else
            {
                foreach (var t in _tradeBook.GetAllTrades()) Trades.Add(TradeRecordRow.FromCore(t));
                LastEventText = "已刷新本地交易簿。";
            }
        }
        catch (Exception ex)
        {
            LastEventText = "刷新失败：" + ex.Message;
        }
    }
}

public sealed class TradeRecordRow
{
    public DateTime Time { get; init; }
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";      // Long / Short
    public string Mode { get; init; } = "";      // DryRun / Testnet
    public decimal Quantity { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal Pnl { get; init; }

    public string ModeText => Mode switch
    {
        "DryRun" => "仿真",
        "Testnet" => "测试网",
        "Live" => "实盘",
        _ => Mode
    };

    public static TradeRecordRow FromCore(TradeRecord t)
    {
        return new TradeRecordRow
        {
            Time = t.CloseTime,
            Symbol = t.Symbol,
            Side = t.Side.ToString(),
            Mode = t.Mode.ToString(),
            Quantity = t.Quantity,
            EntryPrice = t.EntryPrice,
            ExitPrice = t.ExitPrice,
            Pnl = t.RealizedPnl,
        };
    }
}
