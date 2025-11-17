namespace AiFuturesTerminal.Core.Models;

using System;

/// <summary>
/// 账户快照，包含权益、可用余额以及时间戳。
/// </summary>
public sealed class AccountSnapshot
{
    /// <summary>账户权益（净值）。</summary>
    public decimal Equity { get; init; }

    /// <summary>可用余额（可下单金额）。</summary>
    public decimal FreeBalance { get; init; }

    /// <summary>快照时间戳。</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>构造一个账户快照实例。</summary>
    public AccountSnapshot(decimal equity, decimal freeBalance, DateTime timestamp)
    {
        Equity = equity;
        FreeBalance = freeBalance;
        Timestamp = timestamp;
    }
}
