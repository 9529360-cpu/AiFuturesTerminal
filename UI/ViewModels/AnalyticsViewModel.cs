using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Strategy;
using System.Linq;
using System.Threading;

namespace AiFuturesTerminal.UI.ViewModels
{
    public class AnalyticsViewModel : INotifyPropertyChanged
    {
        private readonly TradeAnalyticsService _analytics;

        public ObservableCollection<StrategySummary> StrategySummaries { get; } = new();
        public ObservableCollection<DailyTradeSummaryRow> DailySummaries { get; } = new();
        public ObservableCollection<string> Strategies { get; } = new();

        public DateTime StartDate { get; set; } = DateTime.UtcNow.AddDays(-30);
        public DateTime EndDate { get; set; } = DateTime.UtcNow;

        public string? SelectedStrategy { get; set; }

        public ICommand RefreshCommand { get; }

        public ITradeBook? CurrentTradeBook { get; private set; }

        public AnalyticsViewModel(TradeAnalyticsService analytics)
        {
            _analytics = analytics;
            _analytics.TradeBookChanged += (s, e) => _ = Task.Run(async () => await RefreshAsync());
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => true);

            // populate strategies list from enum (include an empty/All option)
            Strategies.Add(string.Empty); // represents All
            foreach (var kind in Enum.GetValues(typeof(StrategyKind)).Cast<StrategyKind>())
            {
                Strategies.Add(kind.ToString());
            }

            // also populate from persisted trades to include any custom names
            _ = Task.Run(async () =>
            {
                var srows = await _analytics.GetStrategySummaryAsync(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow);
                var names = srows.Select(s => s.StrategyName).OrderBy(n => n).Distinct();
                foreach (var n in names)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (!Strategies.Contains(n)) Strategies.Add(n);
                }
            });

            // set default selected to All
            SelectedStrategy = string.Empty;

            // initial refresh
            _ = Task.Run(async () => await RefreshAsync());
        }

        public async Task RefreshAsync()
        {
            DailySummaries.Clear();
            StrategySummaries.Clear();

            var ds = await _analytics.GetDailySummaryAsync(StartDate, EndDate, SelectedStrategy);
            foreach (var r in ds) DailySummaries.Add(r);

            var ss = await _analytics.GetStrategySummaryAsync(StartDate, EndDate);
            foreach (var r in ss) StrategySummaries.Add(r);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

// The method bodies, field initializers, and property accessor bodies have been eliminated for brevity.
