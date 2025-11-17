namespace AiFuturesTerminal.Core.MarketData;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Models;

/// <summary>
/// 行情服务：从交易所拉取历史 K 线并在内存中缓存按 (Symbol, Interval) 分组的数据。
/// 供策略、回测和终端界面使用统一的数据访问接口。
/// </summary>
public sealed class MarketDataService
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly ConcurrentDictionary<(string Symbol, TimeSpan Interval), List<Candle>> _cache = new();

    /// <summary>
    /// 创建行情服务实例，依赖注入交易所适配器。
    /// </summary>
    public MarketDataService(IExchangeAdapter exchangeAdapter)
    {
        _exchangeAdapter = exchangeAdapter;
    }

    /// <summary>
    /// 加载历史 K 线，优先从内存缓存返回；当缓存不足时从交易所拉取并更新缓存。
    /// </summary>
    /// <param name="symbol">交易对（不区分大小写）。</param>
    /// <param name="interval">K 线间隔。</param>
    /// <param name="limit">期望的最大条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>按时间升序返回的 K 线集合（最新在后）。</returns>
    public async Task<IReadOnlyList<Candle>> LoadHistoricalCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol 不能为空", nameof(symbol));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        symbol = symbol.ToUpperInvariant();
        var key = (Symbol: symbol, Interval: interval);

        // 尝试从缓存获取
        if (_cache.TryGetValue(key, out var existing))
        {
            // 如果缓存已有足够多的数据，直接返回最新的 limit 条
            lock (existing)
            {
                if (existing.Count >= limit)
                {
                    return TakeLastSorted(existing, limit);
                }
            }
        }

        // 缓存不足，从交易所拉取数据
        var fetched = await _exchangeAdapter.GetHistoricalCandlesAsync(symbol, interval, limit, ct).ConfigureAwait(false);
        var list = fetched.ToList();

        // 使用锁保护对缓存的写操作
        var cacheList = _cache.GetOrAdd(key, _ => new List<Candle>());
        lock (cacheList)
        {
            // 用最新拉回的数据替换缓存（简单策略）
            cacheList.Clear();
            cacheList.AddRange(list);
        }

        // 返回最新的 limit 条（fetched 已经是我们需要的）
        return TakeLastSorted(cacheList, limit);
    }

    private static IReadOnlyList<Candle> TakeLastSorted(List<Candle> source, int limit)
    {
        if (source == null || source.Count == 0) return Array.Empty<Candle>();

        var sorted = source.OrderBy(c => c.CloseTime).ToList();
        if (sorted.Count <= limit) return sorted;
        return sorted.Skip(sorted.Count - limit).ToList();
    }
}
