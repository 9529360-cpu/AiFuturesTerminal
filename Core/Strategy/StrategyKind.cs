namespace AiFuturesTerminal.Core.Strategy;

/// <summary>
/// 内置策略类型：剥头皮 / 趋势 / 区间震荡。
/// </summary>
public enum StrategyKind
{
    /// <summary>1 分钟剥头皮动量策略。</summary>
    ScalpingMomentum = 0,

    /// <summary>5-15 分钟趋势跟随策略。</summary>
    TrendFollowing = 1,

    /// <summary>区间震荡 / 均值回归策略。</summary>
    RangeMeanReversion = 2
}
