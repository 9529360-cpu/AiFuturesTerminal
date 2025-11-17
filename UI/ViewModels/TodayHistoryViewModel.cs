using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AiFuturesTerminal.Core.History;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.UI.ViewModels;

public sealed class TodayHistoryViewModel
{
    private readonly IOrderHistoryService _orderHistory;
    private readonly ITradeHistoryService _tradeHistory;
    private readonly IPositionHistoryService _positionHistory;
    private readonly StrategyWatchConfig _watchConfig;

    public ObservableCollection<OrderHistoryRecord> TodayOrders { get; } = new();
    public ObservableCollection<TradeHistoryRecord> TodayTrades { get; } = new();
    public ObservableCollection<PositionHistoryRecord> TodayPositions { get; } = new();

    public ICommand RefreshTodayHistoryCommand { get; }

    public TodayHistoryViewModel(IOrderHistoryService orderHistory, ITradeHistoryService tradeHistory, IPositionHistoryService positionHistory, StrategyWatchConfig watchConfig)
    {
        _orderHistory = orderHistory ?? throw new ArgumentNullException(nameof(orderHistory));
        _tradeHistory = tradeHistory ?? throw new ArgumentNullException(nameof(tradeHistory));
        _positionHistory = positionHistory ?? throw new ArgumentNullException(nameof(positionHistory));
        _watchConfig = watchConfig ?? throw new ArgumentNullException(nameof(watchConfig));

        RefreshTodayHistoryCommand = new RelayCommand(async _ => await RefreshTodayHistoryAsync(), _ => true);
    }

    public async Task RefreshTodayHistoryAsync(CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        var from = utcNow.Date; // today 00:00 UTC

        var query = new HistoryQuery
        {
            From = from,
            To = utcNow,
            Symbol = null,
            StrategyId = null,
            Page = 1,
            PageSize = 1000
        };

        // local temporary lists to dedupe and sort
        var ordersAcc = new List<OrderHistoryRecord>();
        var tradesAcc = new List<TradeHistoryRecord>();
        var positionsAcc = new List<PositionHistoryRecord>();

        // dedupe sets
        var orderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tradeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var posKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // If underlying services require symbol, iterate over watched symbols
        var symbols = _watchConfig.Symbols?.Select(s => s.Symbol).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (symbols != null && symbols.Count > 0)
        {
            // collect per-symbol to support Binance limitations
            foreach (var sym in symbols)
            {
                var q = query with { Symbol = sym };
                try
                {
                    var orders = await _orderHistory.QueryOrdersAsync(q, ct).ConfigureAwait(false);
                    var trades = await _tradeHistory.QueryTradesAsync(q, ct).ConfigureAwait(false);

                    foreach (var o in orders)
                    {
                        var key = $"{o.ExchangeOrderId}:{o.Symbol}";
                        if (orderKeys.Add(key)) ordersAcc.Add(o);
                    }

                    foreach (var t in trades)
                    {
                        var key = $"{t.TradeId}:{t.Symbol}";
                        if (tradeKeys.Add(key)) tradesAcc.Add(t);
                    }
                    try
                    {
                        var positions = await _positionHistory.QueryPositionsAsync(q, ct).ConfigureAwait(false);
                        foreach (var p in positions)
                        {
                            var key = $"{p.Symbol}:{p.OpenTime}:{p.CloseTime}";
                            if (posKeys.Add(key)) positionsAcc.Add(p);
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }
        else
        {
            try
            {
                var orders = await _orderHistory.QueryOrdersAsync(query, ct).ConfigureAwait(false);
                var trades = await _tradeHistory.QueryTradesAsync(query, ct).ConfigureAwait(false);

                foreach (var o in orders)
                {
                    var key = $"{o.ExchangeOrderId}:{o.Symbol}";
                    if (orderKeys.Add(key)) ordersAcc.Add(o);
                }

                foreach (var t in trades)
                {
                    var key = $"{t.TradeId}:{t.Symbol}";
                    if (tradeKeys.Add(key)) tradesAcc.Add(t);
                }
            }
            catch { }
        }

        // sort by time ascending
        var orderedOrders = ordersAcc.OrderBy(o => o.CreateTime).ToArray();
        var orderedTrades = tradesAcc.OrderBy(t => t.Time).ToArray();
        var orderedPositions = positionsAcc.OrderBy(p => p.OpenTime).ToArray();

        // convert times to local before adding
        var localOrders = orderedOrders.Select(o => o with { CreateTime = o.CreateTime.ToLocalTime(), UpdateTime = o.UpdateTime.ToLocalTime() }).ToArray();
        var localTrades = orderedTrades.Select(t => t with { Time = t.Time.ToLocalTime() }).ToArray();
        var localPositions = orderedPositions.Select(p => p with { OpenTime = p.OpenTime.ToLocalTime(), CloseTime = p.CloseTime.ToLocalTime() }).ToArray();

        // update UI collections on dispatcher
        App.Current.Dispatcher.Invoke(() => {
            TodayOrders.Clear();
            TodayTrades.Clear();
            TodayPositions.Clear();

            foreach (var o in localOrders) TodayOrders.Add(o);
            foreach (var t in localTrades) TodayTrades.Add(t);
            foreach (var p in orderedPositions) TodayPositions.Add(p);
        });
    }
}
