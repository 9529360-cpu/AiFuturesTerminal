namespace AiFuturesTerminal.Core.Exchanges;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// 交易所适配器抽象，封装获取历史 K 线、账户信息和下单/平仓等最基本功能。
/// 具体交易所实现（例如 BinanceAdapter）应实现这些方法。
/// </summary>
public interface IExchangeAdapter
{
    /// <summary>
    /// 获取历史 K 线。
    /// </summary>
    /// <param name="symbol">交易对，例如 "BTCUSDT"。</param>
    /// <param name="interval">K 线时间间隔。</param>
    /// <param name="limit">返回的最大条数。</param>
    /// <param name="ct">可选取消令牌。</param>
    /// <returns>按时间升序或任意顺序返回的 K 线列表（调用者不应假设特定排序）。</returns>
    Task<IReadOnlyList<Candle>> GetHistoricalCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default);

    /// <summary>
    /// 获取账户快照（权益、可用余额等）。
    /// </summary>
    Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取指定交易对的当前持仓（如果有），不存在则返回 null。
    /// </summary>
    Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// 下单（市价或限价由实现决定）。
    /// </summary>
    /// <param name="symbol">交易对。</param>
    /// <param name="side">方向（多/空）。</param>
    /// <param name="quantity">数量。</param>
    /// <param name="reason">下单原因或备注。</param>
    Task PlaceOrderAsync(string symbol, PositionSide side, decimal quantity, string reason, CancellationToken ct = default);

    /// <summary>
    /// 平仓指定交易对的持仓。
    /// </summary>
    /// <param name="symbol">交易对。</param>
    /// <param name="reason">平仓原因。</param>
    Task ClosePositionAsync(string symbol, string reason, CancellationToken ct = default);
}
