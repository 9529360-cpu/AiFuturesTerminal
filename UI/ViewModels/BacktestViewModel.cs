using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.IO;
using System.Text;
using System.Globalization;
using Microsoft.Win32;
using AiFuturesTerminal.Core.Backtest;
using ComparisonModel = AiFuturesTerminal.Core.Backtest.Models.StrategyComparisonRow;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.MarketData;

namespace AiFuturesTerminal.UI.ViewModels
{
    public class BacktestViewModel : INotifyPropertyChanged
    {
        private readonly IBacktestService _backtestService = null!;
        private readonly StrategyConfig _strategyConfig = null!;
        private readonly StrategyWatchConfig _watchConfig = null!;

        // cancellation for compare
        private CancellationTokenSource? _compareCts;

        public ObservableCollection<string> AvailableSymbols { get; } = new();

        public IReadOnlyList<StrategyKind> AvailableStrategies { get; private set; }

        public StrategyKind SelectedStrategy { get; set; }
        private string _selectedSymbol = "BTCUSDT";
        public string SelectedSymbol { get => _selectedSymbol; set { _selectedSymbol = value; OnPropertyChanged(); } }

        private DateTime? _startTime;
        public DateTime? StartTime { get => _startTime; set { _startTime = value; OnPropertyChanged(); } }

        private DateTime? _endTime;
        public DateTime? EndTime { get => _endTime; set { _endTime = value; OnPropertyChanged(); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; private set { _isRunning = value; OnPropertyChanged(); } }

        private BacktestSummary? _summary;
        public BacktestSummary? Summary { get => _summary; private set { _summary = value; OnPropertyChanged(); OnPropertyChanged(nameof(NetPnl)); OnPropertyChanged(nameof(MaxDrawdown)); OnPropertyChanged(nameof(TradeCount)); OnPropertyChanged(nameof(WinRate)); } }

        public ObservableCollection<TradeRecord> Trades { get; } = new();

        public ICommand RunCommand { get; }

        // comparison results (use flattened model)
        public ObservableCollection<ComparisonModel> Comparison { get; } = new();
        public ICommand CompareAllStrategiesCommand { get; }
        public ICommand CancelCompareCommand { get; }
        public ICommand ExportComparisonCommand { get; }

        // simple logger delegate that callers (e.g. MainWindow) can set to forward messages to main log view
        public Action<string>? Logger { get; set; }

        // hint about single-request data limit
        public string DataLimitHint => "当前版本单次回测最多使用最近 1500 根 1 分钟K线，时间区间过长时只会回测最后一段数据。";

        // friendly summary properties for UI
        public decimal NetPnl => Summary?.NetPnl ?? 0m;
        public decimal MaxDrawdown => Summary?.MaxDrawdown ?? 0m;
        public int TradeCount => Summary?.TradeCount ?? 0;
        public double WinRate => Summary?.WinRate ?? 0.0;

        // comparing state and current strategy name for UI
        private bool _isComparing;
        public bool IsComparing
        {
            get => _isComparing;
            set
            {
                if (_isComparing == value) return;
                _isComparing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotComparing));
                // update command CanExecute
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsNotComparing => !_isComparing;

        private string _currentStrategyName = string.Empty;
        public string CurrentStrategyName
        {
            get => _currentStrategyName;
            set { _currentStrategyName = value; OnPropertyChanged(); }
        }

        // parameterless ctor for design-time usage
        public BacktestViewModel()
        {
            // initialize available strategies for design-time
            AvailableStrategies = Enum
                .GetValues(typeof(StrategyKind))
                .Cast<StrategyKind>()
                .ToArray();

            SelectedStrategy = AvailableStrategies.FirstOrDefault();

            // allow RunCommand for design-time
            RunCommand = new RelayCommand(async _ => await RunAsync(), _ => true);
            CompareAllStrategiesCommand = new RelayCommand(async _ => await CompareAllStrategiesAsync(), _ => !IsComparing);
            CancelCompareCommand = new RelayCommand(_ => _compareCts?.Cancel(), _ => IsComparing);
            ExportComparisonCommand = new RelayCommand(async _ => await ExportComparisonAsync(), _ => !IsComparing);
        }

        public BacktestViewModel(IBacktestService backtestService, StrategyConfig strategyConfig, StrategyWatchConfig watchConfig)
        {
            _backtestService = backtestService ?? throw new ArgumentNullException(nameof(backtestService));
            _strategyConfig = strategyConfig ?? throw new ArgumentNullException(nameof(strategyConfig));
            _watchConfig = watchConfig ?? throw new ArgumentNullException(nameof(watchConfig));

            // initialize available strategies from enum
            AvailableStrategies = Enum
                .GetValues(typeof(StrategyKind))
                .Cast<StrategyKind>()
                .ToArray();

            // default selected strategy (ensure not null)
            SelectedStrategy = AvailableStrategies.FirstOrDefault();

            StartTime = DateTime.UtcNow.AddDays(-30);
            EndTime = DateTime.UtcNow;

            // populate symbols from watch config
            foreach (var s in _watchConfig.Symbols)
            {
                AvailableSymbols.Add(s.Symbol);
            }

            if (AvailableSymbols.Count > 0) SelectedSymbol = AvailableSymbols[0];

            // commands
            RunCommand = new RelayCommand(async _ => await RunAsync(), _ => true);
            CompareAllStrategiesCommand = new RelayCommand(async _ => await CompareAllStrategiesAsync(), _ => !IsComparing);
            CancelCompareCommand = new RelayCommand(_ => _compareCts?.Cancel(), _ => IsComparing);
            ExportComparisonCommand = new RelayCommand(async _ => await ExportComparisonAsync(), _ => !IsComparing);
        }

        public async Task RunAsync()
        {
            if (!StartTime.HasValue || !EndTime.HasValue) return;

            // if requested span exceeds single-request limit, log a hint
            if ((EndTime.Value - StartTime.Value).TotalMinutes > 1500)
            {
                Logger?.Invoke("[回测] 提示：时间区间超过 1500 分钟，本次仅使用最近 1500 根 1m K 线进行回测。");
            }

            // debug log at start
            Logger?.Invoke($"[回测] RunAsync 开始：Strategy={SelectedStrategy}, Symbol={SelectedSymbol}, Start={StartTime}, End={EndTime}");

            IsRunning = true;
            Trades.Clear();

            var request = new BacktestRequest(SelectedSymbol, SelectedStrategy, StartTime.Value, EndTime.Value, _strategyConfig);
            try
            {
                var res = await _backtestService.RunAsync(request, CancellationToken.None);
                Summary = res.Summary;

                foreach (var t in res.Trades)
                    Trades.Add(t);
            }
            catch (Exception ex)
            {
                // simple error handling - fill empty summary with zeros
                Summary = new BacktestSummary(0m, 0m, 0, 0.0, 0m, 0m, 0m, 0m);
                Logger?.Invoke($"[回测] RunAsync 异常：{ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task CompareAllStrategiesAsync()
        {
            if (IsComparing) return;
            IsComparing = true;
            _compareCts = new CancellationTokenSource();
            Comparison.Clear();

            try
            {
                var strategies = new[]
                {
                    StrategyKind.ScalpingMomentum,
                    StrategyKind.TrendFollowing,
                    StrategyKind.RangeMeanReversion
                };

                int i = 0;
                foreach (var kind in strategies)
                {
                    _compareCts.Token.ThrowIfCancellationRequested();

                    CurrentStrategyName = kind.ToString();
                    var req = new BacktestRequest(SelectedSymbol, kind, StartTime ?? DateTime.UtcNow.AddDays(-30), EndTime ?? DateTime.UtcNow, _strategyConfig);

                    Logger?.Invoke($"[回测] 比较策略 {kind} 开始...");
                    var res = await _backtestService.RunAsync(req, _compareCts.Token);

                    // add flattened model
                    Comparison.Add(new ComparisonModel(kind, res.Summary));
                    Logger?.Invoke($"[回测] 比较策略 {kind} 完成：PnL={res.Summary.NetPnl}, Trades={res.Summary.TradeCount}");

                    i++;
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke("[回测] 比较已被取消");
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"[回测] 比较异常：{ex.Message}");
            }
            finally
            {
                IsComparing = false;
                _compareCts?.Dispose();
                _compareCts = null;
                CurrentStrategyName = string.Empty;
            }
        }

        private async Task ExportComparisonAsync()
        {
            try
            {
                if (Comparison.Count == 0)
                {
                    Logger?.Invoke("[Export] Comparison is empty, nothing to export.");
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter = "CSV 文件 (*.csv)|*.csv",
                    FileName = $"Comparison_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dlg.ShowDialog() != true)
                    return;

                var sb = new StringBuilder();
                sb.AppendLine("StrategyName,NetPnl,ProfitFactor,WinRate,Trades,MaxDrawdown");
                foreach (var r in Comparison)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(r.StrategyName),
                        r.NetPnl.ToString(CultureInfo.InvariantCulture),
                        r.ProfitFactor.ToString(CultureInfo.InvariantCulture),
                        r.WinRate.ToString(CultureInfo.InvariantCulture),
                        r.Trades.ToString(),
                        r.MaxDrawdown.ToString(CultureInfo.InvariantCulture)
                    ));
                }

                await File.WriteAllTextAsync(dlg.FileName, sb.ToString(), Encoding.UTF8);
                Logger?.Invoke($"[Export] Comparison saved: {dlg.FileName}");
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"[Export] Error: {ex.Message}");
            }
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            {
                return '"' + s.Replace("\"", "\"\"") + '"';
            }
            return s;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
