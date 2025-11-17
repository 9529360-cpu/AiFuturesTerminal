using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AiFuturesTerminal.Core.Risk;
using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.UI.ViewModels;

public sealed class GlobalRiskStatusViewModel : INotifyPropertyChanged
{
    private readonly GlobalRiskCoordinator _coordinator;
    private readonly StrategyConfig _strategyConfig;
    private readonly Action<string>? _logger;

    private string _tradesTodayDisplay = string.Empty;
    private string _consecutiveLossDisplay = string.Empty;
    private string _riskStateDisplay = string.Empty;
    private string? _frozenReason;
    private bool _isKillSwitchOn;

    public string TradesTodayDisplay { get => _tradesTodayDisplay; private set { if (_tradesTodayDisplay == value) return; _tradesTodayDisplay = value; OnPropertyChanged(); } }
    public string ConsecutiveLossDisplay { get => _consecutiveLossDisplay; private set { if (_consecutiveLossDisplay == value) return; _consecutiveLossDisplay = value; OnPropertyChanged(); } }
    public string RiskStateDisplay { get => _riskStateDisplay; private set { if (_riskStateDisplay == value) return; _riskStateDisplay = value; OnPropertyChanged(); } }
    public string? FrozenReason { get => _frozenReason; private set { if (_frozenReason == value) return; _frozenReason = value; OnPropertyChanged(); } }

    public bool IsKillSwitchOn { get => _isKillSwitchOn; set { if (_isKillSwitchOn == value) return; _isKillSwitchOn = value; OnPropertyChanged(); ToggleKillSwitchCommand.Execute(value); } }

    public ICommand ToggleKillSwitchCommand { get; }

    public GlobalRiskStatusViewModel(GlobalRiskCoordinator coordinator, StrategyConfig strategyConfig, Action<string>? logger = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _strategyConfig = strategyConfig ?? throw new ArgumentNullException(nameof(strategyConfig));
        _logger = logger;

        ToggleKillSwitchCommand = new RelayCommand(param => {
            bool enable = false;
            if (param is bool b) enable = b;
            _coordinator.SetKillSwitch(enable);
            // log action
            var logMsg = enable ? "[风控] 手动开启 Kill Switch：暂停新开仓" : "[风控] 手动关闭 Kill Switch：恢复新开仓";
            _logger?.Invoke(logMsg);
            Refresh();
        });

        // initial refresh
        Refresh();
    }

    public void Refresh()
    {
        var settings = new GlobalRiskSettings
        {
            RiskPerTrade = _strategyConfig.RiskPerTrade,
            MaxTradesPerDay = _strategyConfig.MaxTradesPerDay,
            MaxConsecutiveLoss = _strategyConfig.MaxConsecutiveLoses
        };

        var snap = _coordinator.GetSnapshot(settings);

        TradesTodayDisplay = snap.MaxTradesPerDay.HasValue ? $"今日成交：{snap.TradesToday} / {snap.MaxTradesPerDay.Value}" : $"今日成交：{snap.TradesToday}";
        ConsecutiveLossDisplay = snap.MaxConsecutiveLoss.HasValue ? $"连续亏损：{snap.ConsecutiveLossCount} / {snap.MaxConsecutiveLoss.Value}" : $"连续亏损：{snap.ConsecutiveLossCount}";
        RiskStateDisplay = snap.IsManualFrozen ? "手动熔断" : (snap.IsFrozen ? "已冻结" : "正常");
        FrozenReason = snap.FrozenReason;
        _isKillSwitchOn = snap.IsManualFrozen;

        OnPropertyChanged(nameof(IsKillSwitchOn));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
