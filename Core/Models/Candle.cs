namespace AiFuturesTerminal.Core.Models;

using System;

/// <summary>
/// 代表一根市场 K 线（烛台），包含时间区间和 OHLCV 数据。
/// </summary>
public readonly record struct Candle
{
    /// <summary>交易对，例如 "BTCUSDT"。</summary>
    public string Symbol { get; init; }

    /// <summary>开盘时间。</summary>
    public DateTime OpenTime { get; init; }

    /// <summary>收盘时间。</summary>
    public DateTime CloseTime { get; init; }

    /// <summary>开盘价。</summary>
    public decimal Open { get; init; }

    /// <summary>最高价。</summary>
    public decimal High { get; init; }

    /// <summary>最低价。</summary>
    public decimal Low { get; init; }

    /// <summary>收盘价。</summary>
    public decimal Close { get; init; }

    /// <summary>成交量。</summary>
    public decimal Volume { get; init; }

    /// <summary>
    /// 返回可读的文本表示，用于日志或调试。
    /// 示例："BTCUSDT 2025-01-01 12:00:00 O:... H:... L:... C:... V:..."
    /// </summary>
    public override string ToString()
    {
        return $"{Symbol} {CloseTime:yyyy-MM-dd HH:mm:ss} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume}";
    }
}
