namespace AiFuturesTerminal.Core;

using System;
using AiFuturesTerminal.Core.Execution;

/// <summary>
/// 运行环境模式，用于在 Mock、Binance U 本位永续 Testnet 与 Live 之间切换。
/// </summary>
public enum EnvironmentMode
{
    Mock = 0,
    BinanceUsdFuturesTestnet = 1,
    BinanceUsdFuturesLive = 2
}

/// <summary>
/// 运行时环境选项，包含环境模式与各类交易所/产品的专用配置。
/// </summary>
public sealed class AppEnvironmentOptions
{
    /// <summary>当前环境模式，默认 Mock。</summary>
    public EnvironmentMode Mode { get; set; } = EnvironmentMode.Mock;

    /// <summary>
    /// 币安 U 本位永续（USDT-M Futures）配置。
    /// </summary>
    public AiFuturesTerminal.Core.Exchanges.Binance.BinanceUsdFuturesOptions BinanceUsdFutures { get; set; } = new();

    /// <summary>当前执行模式（缺省仿真，不实际下单）。</summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.DryRun;

    /// <summary>Enable persisting backtest results to local history store</summary>
    public bool EnableBacktestHistoryPersist { get; init; } = true;

    /// <summary>Binance 持仓对账周期（秒），用于 BinanceStateService 的定时对账，默认 30 秒</summary>
    public int BinancePositionReconcileIntervalSeconds { get; set; } = 30;

    // TODO: EIh723LCksHZ2Y7RQXMByRigFYUe8hYQq41HJ8tzI42giwfHc0K25dRkm77jJLEs / 70SdBlLy3hJBkdv0LqD1iLk2F8GDh7Ck23pzqCcDyQLxLLSZkLsLjlBqKSq7mZ8A / BaseUrl 等配置字段或其它交易所配置

    // Fees & slippage settings (for backtest / dryrun simulation)
    /// <summary>maker fee rate (e.g. -0.00018 = -0.018%)</summary>
    public decimal MakerFeeRate { get; set; } = -0.00018m;

    /// <summary>taker fee rate (e.g. -0.00036 = -0.036%)</summary>
    public decimal TakerFeeRate { get; set; } = -0.00036m;

    /// <summary>滑点：以 Tick 为单位的单边滑点（乘以 TickSize 得到价格偏移）</summary>
    public decimal SlippageTicksPerTrade { get; set; } = 0.5m;

    /// <summary>默认 TickSize（USDT 步长），可由 Adapter 覆盖</summary>
    public decimal SlippageTickSize { get; set; } = 0.01m;

    /// <summary>
    /// 今日累计亏损达到该阈值时冻结开仓（负数），默认 -100 USDT
    /// 可通过配置覆盖
    /// </summary>
    public decimal DailyLossLimit { get; set; } = -100m;
}
