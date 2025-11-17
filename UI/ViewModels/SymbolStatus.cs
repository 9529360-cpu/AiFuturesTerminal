namespace AiFuturesTerminal.UI.ViewModels;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiFuturesTerminal.Core.Strategy;

public sealed class SymbolStatus : INotifyPropertyChanged
{
    private readonly WatchedSymbolConfig _config;

    private bool _enabled;
    private StrategyKind _strategy;
    private string _positionText = "空仓";
    private string _lastAction = "无";
    private string _lastExplanation = string.Empty;
    private string? _riskNote;
    private DateTime _lastUpdated;

    public string Symbol { get; }

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; _config.Enabled = value; OnPropertyChanged(); }
    }

    public StrategyKind Strategy
    {
        get => _strategy;
        set { if (_strategy == value) return; _strategy = value; _config.Kind = value; OnPropertyChanged(); }
    }

    /// <summary>当前持仓文本，用于 UI 显示</summary>
    public string PositionText
    {
        get => _positionText;
        set { if (_positionText == value) return; _positionText = value; OnPropertyChanged(); }
    }

    /// <summary>最近动作，例如 开仓/平仓/准备</summary>
    public string LastAction
    {
        get => _lastAction;
        set { if (_lastAction == value) return; _lastAction = value; OnPropertyChanged(); }
    }

    /// <summary>最近说明</summary>
    public string LastExplanation
    {
        get => _lastExplanation;
        set { if (_lastExplanation == value) return; _lastExplanation = value; OnPropertyChanged(); }
    }

    /// <summary>风控提示（若非空则优先显示）</summary>
    public string? RiskNote
    {
        get => _riskNote;
        set { if (_riskNote == value) return; _riskNote = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayNote)); }
    }

    /// <summary>用于 DataGrid 绑定的显示列：优先展示 RiskNote（若有），否则展示 LastExplanation</summary>
    public string DisplayNote => !string.IsNullOrWhiteSpace(RiskNote) ? RiskNote! : LastExplanation;

    public DateTime LastUpdated
    {
        get => _lastUpdated;
        set { if (_lastUpdated == value) return; _lastUpdated = value; OnPropertyChanged(); }
    }

    public SymbolStatus(WatchedSymbolConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Symbol = config.Symbol;
        _enabled = config.Enabled;
        _strategy = config.Kind;
        _lastUpdated = DateTime.MinValue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
