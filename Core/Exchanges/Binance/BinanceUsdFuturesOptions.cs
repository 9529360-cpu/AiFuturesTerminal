namespace AiFuturesTerminal.Core.Exchanges.Binance;

/// <summary>
/// 币安 U 本位永续（USDT-M Futures）配置。
/// 注意：U 本位永续使用 fapi.binance.com（或 testnet 对应地址），与现货 api.binance.com 区分。
/// </summary>
public sealed class BinanceUsdFuturesOptions
{
    /// <summary>API Key，用于访问币安 U 本位永续。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>API Secret，用于访问币安 U 本位永续。</summary>
    public string ApiSecret { get; init; } = string.Empty;

    /// <summary>是否使用 Testnet，true 表示测试网，false 表示实盘。</summary>
    public bool UseTestnet { get; init; } = true;

    /// <summary>
    /// 可选 BaseAddress，不填时使用 Binance.Net 默认 U 本位永续地址（fapi.binance.com）。
    /// </summary>
    public string? BaseAddress { get; init; }
}
