namespace AiFuturesTerminal.Core.Strategy;

using System.Collections.Generic;

/// <summary>
/// 多币种监控配置（后续可扩展成每个 symbol 多策略开关）。
/// </summary>
public sealed class StrategyWatchConfig
{
    public IList<WatchedSymbolConfig> Symbols { get; } = new List<WatchedSymbolConfig>();
}
