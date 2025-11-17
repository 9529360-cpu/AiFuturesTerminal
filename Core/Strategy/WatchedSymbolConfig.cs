namespace AiFuturesTerminal.Core.Strategy;

/// <summary>
/// 单个交易对的监控配置。
/// </summary>
public sealed class WatchedSymbolConfig
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>是否启用该交易对的监控。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>该交易对使用的策略类型。</summary>
    public StrategyKind Kind { get; set; } = StrategyKind.ScalpingMomentum;
}
