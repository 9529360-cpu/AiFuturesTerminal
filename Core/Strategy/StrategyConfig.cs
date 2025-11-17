namespace AiFuturesTerminal.Core.Strategy;

/// <summary>
/// 策略配置（供智能体决策 + 回测共用）。
/// 先做一版简化的剥头皮/趋势参数。
/// </summary>
public sealed class StrategyConfig
{
    /// <summary>
    /// 当前使用的内置策略类型（默认先用剥头皮）。
    /// </summary>
    public StrategyKind Kind { get; set; } = StrategyKind.ScalpingMomentum;

    // ===== 剥头皮策略参数 =====

    /// <summary>每单风险占权益比例，例如 0.01 = 1%。</summary>
    public decimal RiskPerTrade { get; set; } = 0.01m;

    /// <summary>每笔风险占净值的百分比（优先于 RiskPerTrade），0.01 = 1%</summary>
    public decimal RiskPerTradePct { get; set; } = 0.01m;

    /// <summary>仓位最大名义（USDT）上限，用于保护性限制（默认 10000 USDT）</summary>
    public decimal MaxNotional { get; set; } = 10_000m;

    /// <summary>单笔最大数量（资产单位）上限（默认 10）</summary>
    public decimal MaxQty { get; set; } = 10m;

    /// <summary>最小下单步长（资产单位），USDT 合约默认 0.001</summary>
    public decimal MinQtyStep { get; set; } = 0.001m;

    /// <summary>止损倍数 R。</summary>
    public decimal StopLossRMultiple { get; set; } = 1.0m;

    /// <summary>止盈倍数 R。</summary>
    public decimal TakeProfitRMultiple { get; set; } = 2.0m;

    /// <summary>短周期均线长度（剥头皮用，例如 9）。</summary>
    public int FastMaLength { get; set; } = 9;

    /// <summary>长周期均线长度（剥头皮用，例如 21）。</summary>
    public int SlowMaLength { get; set; } = 21;

    /// <summary>日内最大开仓次数。</summary>
    public int MaxTradesPerDay { get; set; } = 10;

    /// <summary>允许的最大连续亏损次数，超过后当日停止开新仓。</summary>
    public int MaxConsecutiveLoses { get; set; } = 3;

    /// <summary>剥头皮策略单笔最长持仓时间（分钟）。</summary>
    public int ScalpingTimeoutMinutes { get; set; } = 10;

    // ===== 趋势跟随策略参数（5-15 分钟级别） =====

    /// <summary>趋势策略中用于判断方向的快均线长度（例如 50）。</summary>
    public int TrendFastMaLength { get; set; } = 50;

    /// <summary>趋势策略中用于判断大方向的慢均线长度（例如 200）。</summary>
    public int TrendSlowMaLength { get; set; } = 200;

    /// <summary>趋势策略的止盈 R 倍数（默认 3R）。</summary>
    public decimal TrendTakeProfitRMultiple { get; set; } = 3.0m;

    /// <summary>趋势策略的止损 R 倍数（默认 1R）。</summary>
    public decimal TrendStopLossRMultiple { get; set; } = 1.0m;

    /// <summary>趋势策略最大持仓时间（分钟），默认 4 小时 = 240 分钟。</summary>
    public int TrendMaxHoldingMinutes { get; set; } = 240;

    /// <summary>用于 ATR 计算的周期，默认 14。</summary>
    public int AtrPeriod { get; set; } = 14;

    // ===== 区间震荡 / 均值回归策略参数（布林带风格） =====

    /// <summary>布林带周期（例如 20）。</summary>
    public int RangePeriod { get; set; } = 20;

    /// <summary>布林带宽度（标准差倍数，例如 2.0）。</summary>
    public decimal RangeBandWidth { get; set; } = 2.0m;

    /// <summary>区间策略的止盈 R 倍数（默认约 1.5R，当价格回到中轨附近即平仓）。</summary>
    public decimal RangeTakeProfitRMultiple { get; set; } = 1.5m;

    /// <summary>区间策略的止损 R 倍数（默认 1R，当有效突破通道时止损）。</summary>
    public decimal RangeStopLossRMultiple { get; set; } = 1.0m;

    /// <summary>区间策略最大持仓时间（分钟），默认 60 分钟。</summary>
    public int RangeMaxHoldingMinutes { get; set; } = 60;

    /// <summary>用于 RSI 计算的周期，默认 14。</summary>
    public int RsiPeriod { get; set; } = 14;
}
