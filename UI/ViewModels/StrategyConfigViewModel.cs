namespace AiFuturesTerminal.UI.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiFuturesTerminal.Core.Strategy;

public sealed class StrategyConfigViewModel : INotifyPropertyChanged
{
    private readonly StrategyConfig _model;
    private readonly StrategyConfigService _service;

    public StrategyConfigViewModel(StrategyConfig model, StrategyConfigService service)
    {
        _model = model ?? throw new System.ArgumentNullException(nameof(model));
        _service = service ?? throw new System.ArgumentNullException(nameof(service));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public decimal RiskPerTrade
    {
        get => _model.RiskPerTrade;
        set
        {
            if (_model.RiskPerTrade == value) return;
            _model.RiskPerTrade = value;
            OnPropertyChanged();
        }
    }

    public decimal RiskPerTradePct
    {
        get => _model.RiskPerTradePct;
        set { if (_model.RiskPerTradePct == value) return; _model.RiskPerTradePct = value; OnPropertyChanged(); }
    }

    public decimal MaxNotional
    {
        get => _model.MaxNotional;
        set { if (_model.MaxNotional == value) return; _model.MaxNotional = value; OnPropertyChanged(); }
    }

    public decimal MaxQty
    {
        get => _model.MaxQty;
        set { if (_model.MaxQty == value) return; _model.MaxQty = value; OnPropertyChanged(); }
    }

    public decimal MinQtyStep
    {
        get => _model.MinQtyStep;
        set { if (_model.MinQtyStep == value) return; _model.MinQtyStep = value; OnPropertyChanged(); }
    }

    public int MaxTradesPerDay
    {
        get => _model.MaxTradesPerDay;
        set
        {
            if (_model.MaxTradesPerDay == value) return;
            _model.MaxTradesPerDay = value;
            OnPropertyChanged();
        }
    }

    public int MaxConsecutiveLoses
    {
        get => _model.MaxConsecutiveLoses;
        set
        {
            if (_model.MaxConsecutiveLoses == value) return;
            _model.MaxConsecutiveLoses = value;
            OnPropertyChanged();
        }
    }

    // Scalping
    public int FastMaLength
    {
        get => _model.FastMaLength;
        set { if (_model.FastMaLength == value) return; _model.FastMaLength = value; OnPropertyChanged(); }
    }

    public int SlowMaLength
    {
        get => _model.SlowMaLength;
        set { if (_model.SlowMaLength == value) return; _model.SlowMaLength = value; OnPropertyChanged(); }
    }

    public decimal StopLossRMultiple
    {
        get => _model.StopLossRMultiple;
        set { if (_model.StopLossRMultiple == value) return; _model.StopLossRMultiple = value; OnPropertyChanged(); }
    }

    public decimal TakeProfitRMultiple
    {
        get => _model.TakeProfitRMultiple;
        set { if (_model.TakeProfitRMultiple == value) return; _model.TakeProfitRMultiple = value; OnPropertyChanged(); }
    }

    public int ScalpingTimeoutMinutes
    {
        get => _model.ScalpingTimeoutMinutes;
        set { if (_model.ScalpingTimeoutMinutes == value) return; _model.ScalpingTimeoutMinutes = value; OnPropertyChanged(); }
    }

    // Trend
    public int TrendFastMaLength
    {
        get => _model.TrendFastMaLength;
        set { if (_model.TrendFastMaLength == value) return; _model.TrendFastMaLength = value; OnPropertyChanged(); }
    }

    public int TrendSlowMaLength
    {
        get => _model.TrendSlowMaLength;
        set { if (_model.TrendSlowMaLength == value) return; _model.TrendSlowMaLength = value; OnPropertyChanged(); }
    }

    public decimal TrendTakeProfitRMultiple
    {
        get => _model.TrendTakeProfitRMultiple;
        set { if (_model.TrendTakeProfitRMultiple == value) return; _model.TrendTakeProfitRMultiple = value; OnPropertyChanged(); }
    }

    public decimal TrendStopLossRMultiple
    {
        get => _model.TrendStopLossRMultiple;
        set { if (_model.TrendStopLossRMultiple == value) return; _model.TrendStopLossRMultiple = value; OnPropertyChanged(); }
    }

    public int TrendMaxHoldingMinutes
    {
        get => _model.TrendMaxHoldingMinutes;
        set { if (_model.TrendMaxHoldingMinutes == value) return; _model.TrendMaxHoldingMinutes = value; OnPropertyChanged(); }
    }

    public int AtrPeriod
    {
        get => _model.AtrPeriod;
        set { if (_model.AtrPeriod == value) return; _model.AtrPeriod = value; OnPropertyChanged(); }
    }

    // Range
    public int RangePeriod
    {
        get => _model.RangePeriod;
        set { if (_model.RangePeriod == value) return; _model.RangePeriod = value; OnPropertyChanged(); }
    }

    public decimal RangeBandWidth
    {
        get => _model.RangeBandWidth;
        set { if (_model.RangeBandWidth == value) return; _model.RangeBandWidth = value; OnPropertyChanged(); }
    }

    public decimal RangeTakeProfitRMultiple
    {
        get => _model.RangeTakeProfitRMultiple;
        set { if (_model.RangeTakeProfitRMultiple == value) return; _model.RangeTakeProfitRMultiple = value; OnPropertyChanged(); }
    }

    public decimal RangeStopLossRMultiple
    {
        get => _model.RangeStopLossRMultiple;
        set { if (_model.RangeStopLossRMultiple == value) return; _model.RangeStopLossRMultiple = value; OnPropertyChanged(); }
    }

    public int RangeMaxHoldingMinutes
    {
        get => _model.RangeMaxHoldingMinutes;
        set { if (_model.RangeMaxHoldingMinutes == value) return; _model.RangeMaxHoldingMinutes = value; OnPropertyChanged(); }
    }

    public int RsiPeriod
    {
        get => _model.RsiPeriod;
        set { if (_model.RsiPeriod == value) return; _model.RsiPeriod = value; OnPropertyChanged(); }
    }

    public void Save()
    {
        _service.SaveAsync(_model).GetAwaiter().GetResult();
    }
}
