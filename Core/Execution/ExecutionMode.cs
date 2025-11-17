namespace AiFuturesTerminal.Core.Execution;

/// <summary>
/// 执行模式：回测 / 仿真 / 实盘
/// </summary>
public enum ExecutionMode
{
    /// <summary>仅回放/回测模式（历史数据仿真）</summary>
    Backtest = 0,

    /// <summary>DryRun：仿真下单（通常使用 Mock adapter）</summary>
    DryRun = 1,

    /// <summary>Testnet：使用交易所测试网络</summary>
    Testnet = 2,

    /// <summary>Live：真实交易</summary>
    Live = 3
}
