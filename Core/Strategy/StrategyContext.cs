namespace AiFuturesTerminal.Core.Strategy;

using System;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// 策略运行时上下文，不可变（init-only 属性）
/// </summary>
public sealed class StrategyContext
{
    /// <summary>当前时间点（允许使用 DateTimeOffset 提供时区感知）</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>当前 K 线</summary>
    public Candle CurrentBar { get; init; }

    /// <summary>历史 K 线序列（不包含 CurrentBar 或 包含，视调用方约定）</summary>
    public IReadOnlyList<Candle> History { get; init; } = Array.Empty<Candle>();

    /// <summary>当前持仓（如果有）</summary>
    public Position? CurrentPosition { get; init; }

    /// <summary>账户快照信息（权益等）</summary>
    public AccountSnapshot Account { get; init; } = new AccountSnapshot(0m, 0m, DateTime.UtcNow);

    /// <summary>运行时附加信息字典，可用于传递额外上下文</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// 无参构造，便于初始化后使用 init 属性进行赋值
    /// </summary>
    public StrategyContext() { }

    /// <summary>
    /// 便利构造器
    /// </summary>
    public StrategyContext(DateTimeOffset now, Candle currentBar, IReadOnlyList<Candle> history,
                           Position? currentPosition, AccountSnapshot account)
    {
        Now = now;
        CurrentBar = currentBar;
        History = history ?? Array.Empty<Candle>();
        CurrentPosition = currentPosition;
        Account = account ?? new AccountSnapshot(0m, 0m, DateTime.UtcNow);
    }
}
