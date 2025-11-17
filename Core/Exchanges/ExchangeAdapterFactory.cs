namespace AiFuturesTerminal.Core.Exchanges;

using System;
using AiFuturesTerminal.Core;
using AiFuturesTerminal.Core.Exchanges.Binance;

/// <summary>
/// 交易所适配器工厂，根据运行时环境返回对应的 IExchangeAdapter 实例。
/// </summary>
public static class ExchangeAdapterFactory
{
    /// <summary>
    /// 创建一个 IExchangeAdapter 实例，基于 AppEnvironmentOptions 中的 Mode。
    /// </summary>
    public static IExchangeAdapter Create(AppEnvironmentOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        return options.Mode switch
        {
            EnvironmentMode.Mock => new Mock.MockExchangeAdapter(),
            EnvironmentMode.BinanceUsdFuturesTestnet => new BinanceAdapter(new BinanceUsdFuturesOptions
            {
                ApiKey = options.BinanceUsdFutures.ApiKey,
                ApiSecret = options.BinanceUsdFutures.ApiSecret,
                UseTestnet = true,
                BaseAddress = options.BinanceUsdFutures.BaseAddress
            }),
            EnvironmentMode.BinanceUsdFuturesLive => new BinanceAdapter(new BinanceUsdFuturesOptions
            {
                ApiKey = options.BinanceUsdFutures.ApiKey,
                ApiSecret = options.BinanceUsdFutures.ApiSecret,
                UseTestnet = false,
                BaseAddress = options.BinanceUsdFutures.BaseAddress
            }),
            _ => throw new NotSupportedException("未知的 EnvironmentMode: " + options.Mode)
        };
    }
}
