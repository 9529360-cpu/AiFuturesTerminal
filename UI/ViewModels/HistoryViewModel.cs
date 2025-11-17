using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AiFuturesTerminal.Core.History;

namespace AiFuturesTerminal.UI.ViewModels;

public sealed partial class HistoryViewModel : INotifyPropertyChanged
{
    private readonly IOrderHistoryService _orderHistory;
    private readonly ITradeHistoryService _tradeHistory;
    private readonly IPositionHistoryService _positionHistory;

    public ObservableCollection<PositionHistoryRecord> Positions { get; } = new();
    public ObservableCollection<OrderHistoryRecord> Orders { get; } = new();
    public ObservableCollection<TradeHistoryRecord> Trades { get; } = new();

    public DateTime FromDate { get; set; } = DateTime.UtcNow.Date.AddDays(-7);
    public DateTime ToDate { get; set; } = DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1);

    public string? SelectedSymbol { get; set; }
    public string? SelectedStrategyId { get; set; }

    public IReadOnlyList<string> Symbols { get; } = Array.Empty<string>();
    public IReadOnlyList<string> StrategyIds { get; } = Array.Empty<string>();

    public ICommand LoadHistoryCommand { get; } = null!;

    public HistoryViewModel(IOrderHistoryService orderHistory, ITradeHistoryService tradeHistory, IPositionHistoryService positionHistory)
    {
        _orderHistory = orderHistory ?? throw new ArgumentNullException(nameof(orderHistory));
        _tradeHistory = tradeHistory ?? throw new ArgumentNullException(nameof(tradeHistory));
        _positionHistory = positionHistory ?? throw new ArgumentNullException(nameof(positionHistory));

        LoadHistoryCommand = new RelayCommand(async _ => await LoadAsync(), _ => true);
    }

    private async Task LoadAsync()
    {
        var query = new HistoryQuery
        {
            From = FromDate,
            To = ToDate,
            Symbol = SelectedSymbol,
            StrategyId = SelectedStrategyId,
            Page = 1,
            PageSize = 1000
        };

        Positions.Clear(); Orders.Clear(); Trades.Clear();

        var ps = await _positionHistory.QueryPositionsAsync(query).ConfigureAwait(false);
        var os = await _orderHistory.QueryOrdersAsync(query).ConfigureAwait(false);
        var ts = await _tradeHistory.QueryTradesAsync(query).ConfigureAwait(false);

        App.Current.Dispatcher.Invoke(() => {
            foreach (var p in ps) Positions.Add(p);
            foreach (var o in os) Orders.Add(o);
            foreach (var t in ts) Trades.Add(t);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
