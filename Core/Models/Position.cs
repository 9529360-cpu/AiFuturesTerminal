namespace AiFuturesTerminal.Core.Models;

using System;

/// <summary>
/// 表示一个持仓（或历史持仓）对象。
/// </summary>
public enum PositionSide { Flat, Long, Short }

/// <summary>
/// 代表账户中的一个仓位，包含方向、数量、开仓价和时间等信息。
/// </summary>
public sealed class Position
{
    /// <summary>交易对符号，例如 "BTCUSDT"。</summary>
    public string Symbol { get; set; }

    /// <summary>仓位方向。</summary>
    public PositionSide Side { get; set; }

    /// <summary>仓位数量（合约或币的数量）。</summary>
    public decimal Quantity { get; set; }

    /// <summary>开仓均价。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>开仓时间（如果有）。</summary>
    public DateTime? EntryTime { get; set; }

    /// <summary>无参构造，初始化为空仓状态。</summary>
    public Position()
    {
        Symbol = string.Empty;
        Side = PositionSide.Flat;
        Quantity = 0m;
        EntryPrice = 0m;
        EntryTime = null;
    }

    /// <summary>使用交易对符号创建一个空仓位对象。</summary>
    /// <param name="symbol">交易对符号。</param>
    public Position(string symbol) : this()
    {
        Symbol = symbol;
    }

    /// <summary>判断当前是否为空仓（Flat 或 数量为 0）。</summary>
    public bool IsFlat() => Side == PositionSide.Flat || Quantity == 0m;

    /// <summary>
    /// 计算按当前市价（lastPrice）的未实现盈亏。
    /// Flat 返回 0；Long: (lastPrice - EntryPrice) * Quantity；Short: (EntryPrice - lastPrice) * Quantity。
    /// </summary>
    /// <param name="lastPrice">当前市价。</param>
    /// <returns>未实现盈亏。</returns>
    public decimal GetUnrealizedPnl(decimal lastPrice)
    {
        if (IsFlat()) return 0m;

        return Side switch
        {
            PositionSide.Long => (lastPrice - EntryPrice) * Quantity,
            PositionSide.Short => (EntryPrice - lastPrice) * Quantity,
            _ => 0m
        };
    }
}
