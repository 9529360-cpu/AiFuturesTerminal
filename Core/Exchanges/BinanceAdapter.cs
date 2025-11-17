namespace AiFuturesTerminal.Core.Exchanges.Binance;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Execution;
using System.Globalization;
using Microsoft.Extensions.Logging;

/// <summary>
/// Binance U 本位永续（USDT-M Futures）适配器，使用轻量 HTTP 实现（支持 Testnet / Live via BaseAddress）。
/// 注意：历史 K 线使用公开接口；账户与持仓使用签名接口，需在 AppEnvironmentOptions 中提供 ApiKey / ApiSecret。
/// </summary>
public sealed class BinanceAdapter : IExchangeAdapter
{
    private readonly BinanceUsdFuturesOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _baseAddress;
    private readonly ILogger<BinanceAdapter>? _logger;

    public BinanceAdapter(BinanceUsdFuturesOptions options, ILogger<BinanceAdapter>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        // 默认 U 本位永续地址
        _baseAddress = !string.IsNullOrWhiteSpace(options.BaseAddress)
            ? new Uri(options.BaseAddress)
            : new Uri("https://fapi.binance.com");

        _http = new HttpClient { BaseAddress = _baseAddress };

        // If ApiKey provided, set header for unsigned calls that still require API key (account endpoints require it)
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Remove("X-MBX-APIKEY");
            _http.DefaultRequestHeaders.Add("X-MBX-APIKEY", options.ApiKey);
        }
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

    private static string Sign(string data, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        using var hmac = new HMACSHA256(key);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(sig).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// 创建一个带签名的 GET 请求，用于 Binance U 本位永续的 SIGNED endpoints。
    /// </summary>
    private HttpRequestMessage CreateSignedGetRequest(string path, IDictionary<string, string>? extraParams = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException("调用签名接口前必须配置 ApiKey/ApiSecret。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var parameters = new List<string>();

        if (extraParams is not null)
        {
            foreach (var kv in extraParams)
            {
                var value = Uri.EscapeDataString(kv.Value ?? string.Empty);
                parameters.Add($"{kv.Key}={value}");
            }
        }

        // 基本参数：timestamp + recvWindow
        parameters.Add($"timestamp={now}");
        parameters.Add("recvWindow=5000");

        var queryString = string.Join("&", parameters);

        // 计算签名：HMAC-SHA256(secret, queryString)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ApiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
        var signature = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

        var fullPath = $"{path}?{queryString}&signature={signature}";

        var request = new HttpRequestMessage(HttpMethod.Get, fullPath);
        request.Headers.Add("X-MBX-APIKEY", _options.ApiKey!);
        return request;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Candle>> GetHistoricalCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol 不能为空", nameof(symbol));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit 必须 > 0");

        symbol = symbol.ToUpperInvariant();
        // map interval to Binance interval string
        string intervalStr = interval switch
        {
            { TotalMinutes: 1 } => "1m",
            { TotalMinutes: 3 } => "3m",
            { TotalMinutes: 5 } => "5m",
            { TotalMinutes: 15 } => "15m",
            { TotalMinutes: 30 } => "30m",
            { TotalHours: 1 } => "1h",
            { TotalHours: 4 } => "4h",
            { TotalDays: 1 } => "1d",
            _ => throw new NotSupportedException($"不支持的 K 线周期: {interval}")
        };

        var url = $"/fapi/v1/klines?symbol={symbol}&interval={intervalStr}&limit={limit}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取 U 本位永续 K 线失败: HTTP {resp.StatusCode} {txt}");
        }

        // response is json array of arrays
        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;
        var list = new List<Candle>();
        foreach (var item in root.EnumerateArray())
        {
            // kline format: [ openTime, open, high, low, close, volume, closeTime, ... ]
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).UtcDateTime;
            var open = decimal.Parse(item[1].GetString() ?? "0");
            var high = decimal.Parse(item[2].GetString() ?? "0");
            var low = decimal.Parse(item[3].GetString() ?? "0");
            var close = decimal.Parse(item[4].GetString() ?? "0");
            var volume = decimal.Parse(item[5].GetString() ?? "0");
            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64()).UtcDateTime;

            list.Add(new Candle
            {
                Symbol = symbol,
                OpenTime = openTime,
                CloseTime = closeTime,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        return list.OrderBy(c => c.OpenTime).ToList();
    }

    private static bool TryGetDecimalFromJsonElement(JsonElement el, out decimal value)
    {
        value = 0m;
        try
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                value = el.GetDecimal();
                return true;
            }
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (decimal.TryParse(s, out value)) return true;
            }
        }
        catch
        {
            // ignore parse errors
        }
        return false;
    }

    /// <summary>
    /// 获取 U 本位永续账户快照（fapi/v2/account）。需要 ApiKey/ApiSecret。
    /// </summary>
    public async Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default)
    {
        // Use signed GET request builder
        using var request = CreateSignedGetRequest("/fapi/v2/account");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var txt = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取 U 本位永续账户信息失败: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {txt}");
        }

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        decimal equity = 0m;
        decimal free = 0m;

        // try to find totalWalletBalance or assets with USDT
        if (root.TryGetProperty("totalWalletBalance", out var totalWallet))
        {
            if (!TryGetDecimalFromJsonElement(totalWallet, out equity)) equity = 0m;
        }

        if (root.TryGetProperty("availableBalance", out var avail))
        {
            if (!TryGetDecimalFromJsonElement(avail, out free)) free = 0m;
        }

        // if assets array present, try find USDT entry
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (a.TryGetProperty("asset", out var assetName) && string.Equals(assetName.GetString(), "USDT", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.TryGetProperty("walletBalance", out var wb) && TryGetDecimalFromJsonElement(wb, out var wbVal)) equity = wbVal;
                    if (a.TryGetProperty("availableBalance", out var ab) && TryGetDecimalFromJsonElement(ab, out var abVal)) free = abVal;
                    break;
                }
            }
        }

        return new AccountSnapshot(equity, free, DateTime.UtcNow);
    }

    /// <summary>
    /// 获取指定 symbol 的净持仓（fapi/v2/positionRisk?symbol=XXX）。返回 null 表示无持仓或仓位为 0。
    /// </summary>
    public async Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
        {
            throw new InvalidOperationException("获取 U 本位永续持仓失败: 未提供 ApiKey/ApiSecret");
        }

        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol 不能为空", nameof(symbol));
        symbol = symbol.ToUpperInvariant();

        var ts = GetTimestamp();
        var query = $"symbol={symbol}&timestamp={ts}";
        var signature = Sign(query, _options.ApiSecret);
        var url = $"/fapi/v2/positionRisk?{query}&signature={signature}";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取 U 本位永续持仓失败: HTTP {resp.StatusCode} {txt}");
        }

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        // root is array of position info
        foreach (var p in root.EnumerateArray())
        {
            if (p.TryGetProperty("symbol", out var ps) && string.Equals(ps.GetString(), symbol, StringComparison.OrdinalIgnoreCase))
            {
                // positionAmt (string), entryPrice (string)
                var posAmtStr = p.GetProperty("positionAmt").GetString() ?? "0";
                var entryPriceStr = p.GetProperty("entryPrice").GetString() ?? "0";

                if (!decimal.TryParse(posAmtStr, out var posAmt)) posAmt = 0m;
                if (!decimal.TryParse(entryPriceStr, out var entryPrice)) entryPrice = 0m;

                if (posAmt == 0m) return null;

                var side = posAmt > 0 ? PositionSide.Long : PositionSide.Short;
                var qty = Math.Abs(posAmt);

                var pos = new Position(symbol)
                {
                    Side = side,
                    Quantity = qty,
                    EntryPrice = entryPrice,
                    EntryTime = null
                };

                return pos;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取指定 symbol 的仓位风险信息（调用 fapi/v2/positionRisk 或 fapi/v3/positionRisk）并返回 Position or null
    /// </summary>
    public async Task<Position?> GetPositionRiskAsync(string symbol, CancellationToken ct = default)
    {
        // delegate to existing GetOpenPositionAsync implementation
        return await GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取当前用户在合约市场的未成交订单（open orders）
    /// </summary>
    public async Task<JsonDocument?> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default)
    {
        var path = "/fapi/v1/openOrders";
        var extra = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(symbol)) extra["symbol"] = symbol.ToUpperInvariant();

        using var req = CreateSignedGetRequest(path, extra);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GetOpenOrders failed: HTTP {resp.StatusCode} {txt}");
        return JsonDocument.Parse(txt);
    }

    /// <summary>
    /// 获取账户余额（同 GetAccountSnapshotAsync，但返回原始 JsonDocument 以便上层决定解析策略）
    /// </summary>
    public async Task<JsonDocument> GetAccountBalanceAsync(CancellationToken ct = default)
    {
        using var req = CreateSignedGetRequest("/fapi/v2/account");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GetAccountBalance failed: HTTP {resp.StatusCode} {txt}");
        return JsonDocument.Parse(txt);
    }

    /// <summary>
    /// 拉取 income（日内/历史）记录，用于获取已实现 pnl 和手续费
    /// </summary>
    public async Task<IReadOnlyList<IncomeEntry>> GetIncomeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var list = new List<IncomeEntry>();
        var startTime = new DateTimeOffset(fromUtc).ToUnixTimeMilliseconds();
        var endTime = new DateTimeOffset(toUtc).ToUnixTimeMilliseconds();

        var extra = new Dictionary<string, string>
        {
            ["incomeType"] = "REALIZED_PNL",
            ["startTime"] = startTime.ToString(),
            ["endTime"] = endTime.ToString()
        };

        using var req = CreateSignedGetRequest("/fapi/v1/income", extra);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GetIncome failed: HTTP {resp.StatusCode} {txt}");

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
            {
                try
                {
                    var ie = new IncomeEntry();
                    ie.Symbol = el.GetProperty("symbol").GetString();
                    ie.IncomeType = el.GetProperty("incomeType").GetString() ?? string.Empty;
                    ie.Income = el.TryGetProperty("income", out var incEl) && incEl.ValueKind != JsonValueKind.Null ? decimal.Parse(incEl.GetString() ?? "0", CultureInfo.InvariantCulture) : 0m;
                    ie.Commission = el.TryGetProperty("commission", out var cEl) && cEl.ValueKind != JsonValueKind.Null ? decimal.Parse(cEl.GetString() ?? "0", CultureInfo.InvariantCulture) : 0m;
                    ie.PositionSide = el.TryGetProperty("positionSide", out var ps) ? ps.GetString() ?? string.Empty : string.Empty;
                    ie.Quantity = 0m;
                    if (el.TryGetProperty("time", out var t)) ie.Time = DateTimeOffset.FromUnixTimeMilliseconds(t.GetInt64()).UtcDateTime;
                    list.Add(ie);
                }
                catch { }
            }
        }

        return list;
    }

    /// <summary>
    /// 拉取指定 symbol 的 userTrades（逐笔成交）以便需要时进行更细粒度的同步
    /// </summary>
    public async Task<JsonDocument?> GetUserTradesAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol required", nameof(symbol));
        var extra = new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        if (startTime.HasValue) extra["startTime"] = new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds().ToString();
        if (endTime.HasValue) extra["endTime"] = new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds().ToString();

        using var req = CreateSignedGetRequest("/fapi/v1/userTrades", extra);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GetUserTrades failed: HTTP {resp.StatusCode} {txt}");
        return JsonDocument.Parse(txt);
    }

    /// <summary>
    /// Get all orders (historical) for a symbol via /fapi/v1/allOrders
    /// </summary>
    public async Task<JsonDocument?> GetAllOrdersAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol required", nameof(symbol));
        var extra = new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        if (startTime.HasValue) extra["startTime"] = new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds().ToString();
        if (endTime.HasValue) extra["endTime"] = new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds().ToString();

        using var req = CreateSignedGetRequest("/fapi/v1/allOrders", extra);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GetAllOrders failed: HTTP {resp.StatusCode} {txt}");
        return JsonDocument.Parse(txt);
    }

    // new helper type for income parsing
    public sealed class IncomeEntry
    {
        public string? Symbol { get; set; }
        public string IncomeType { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Commission { get; set; }
        public string PositionSide { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public DateTime Time { get; set; }
        public ExecutionMode Mode { get; set; } = ExecutionMode.Testnet;
    }

    /// <inheritdoc />
    public async Task PlaceOrderAsync(string symbol, PositionSide side, decimal quantity, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException("下单失败: 未配置 ApiKey/ApiSecret");

        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol 不能为空", nameof(symbol));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "quantity 必须 > 0");

        symbol = symbol.ToUpperInvariant();
        var sideStr = side == PositionSide.Long ? "BUY" : "SELL";

        var ts = GetTimestamp();
        var quantityStr = quantity.ToString(CultureInfo.InvariantCulture);

        var parameters = new List<string>
        {
            $"symbol={symbol}",
            $"side={sideStr}",
            "type=MARKET",
            $"quantity={Uri.EscapeDataString(quantityStr)}",
            $"timestamp={ts}",
            "recvWindow=5000"
        };

        var queryString = string.Join("&", parameters);
        var signature = Sign(queryString, _options.ApiSecret);
        var url = $"/fapi/v1/order?{queryString}&signature={signature}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-MBX-APIKEY", _options.ApiKey!);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Binance 下单失败：HTTP {(int)resp.StatusCode} {txt}");
        }

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.GetInt32() < 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : txt;
            throw new InvalidOperationException($"Binance 下单失败：{msg}");
        }

        // success - no further action required for now
    }

    /// <inheritdoc />
    public async Task ClosePositionAsync(string symbol, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException("平仓失败: 未配置 ApiKey/ApiSecret");

        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("symbol 不能为空", nameof(symbol));

        symbol = symbol.ToUpperInvariant();

        // get open position
        var pos = await GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);
        if (pos == null || pos.IsFlat())
            return; // nothing to do

        var sideStr = pos.Side == PositionSide.Long ? "SELL" : "BUY"; // to close long -> sell, to close short -> buy
        var quantityStr = pos.Quantity.ToString(CultureInfo.InvariantCulture);

        var ts = GetTimestamp();
        var parameters = new List<string>
        {
            $"symbol={symbol}",
            $"side={sideStr}",
            "type=MARKET",
            $"quantity={Uri.EscapeDataString(quantityStr)}",
            "reduceOnly=true",
            $"timestamp={ts}",
            "recvWindow=5000"
        };

        var queryString = string.Join("&", parameters);
        var signature = Sign(queryString, _options.ApiSecret);
        var url = $"/fapi/v1/order?{queryString}&signature={signature}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-MBX-APIKEY", _options.ApiKey!);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Binance 平仓失败：HTTP {(int)resp.StatusCode} {txt}");
        }

        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.GetInt32() < 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : txt;
            throw new InvalidOperationException($"Binance 平仓失败：{msg}");
        }

        // success
    }

    // Start user data stream and listen for ORDER_TRADE_UPDATE events
    public async Task StartUserDataStreamAsync(CancellationToken externalCt = default)
    {
        // ensure only one background manager runs
        lock (_udsLock)
        {
            if (_userDataTask != null && !_userDataTask.IsCompleted)
                return; // already running
            // create linked token source
            _userDataCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _userDataTask = Task.Run(() => RunUserDataStreamManagerAsync(_userDataCts.Token));
        }

        // return immediately; background manager handles lifecycle
        await Task.CompletedTask;
    }

    private readonly object _udsLock = new();
    private CancellationTokenSource? _userDataCts;
    private Task? _userDataTask;

    private async Task RunUserDataStreamManagerAsync(CancellationToken ct)
    {
        const int maxReconnectAttempts = 10;

        while (!ct.IsCancellationRequested)
        {
            string? listenKey = null;
            try
            {
                listenKey = await CreateListenKeyAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { _logger?.LogError($"[币安] 创建 listenKey 失败: {ex.Message}"); } catch { }
                // wait and retry
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(listenKey))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                try { _logger?.LogInformation($"[币安] 用户数据流已启动，listenKey={listenKey}"); } catch { }

                using var ws = new System.Net.WebSockets.ClientWebSocket();
                var wsUrl = new UriBuilder(_baseAddress.Scheme == "https" ? "wss" : "ws", _baseAddress.Host)
                {
                    Path = $"/ws/{listenKey}"
                }.Uri;

                await ws.ConnectAsync(wsUrl, ct).ConfigureAwait(false);

                // start keepalive task
                using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var keepaliveTask = Task.Run(() => KeepAliveLoopAsync(listenKey, keepaliveCts.Token));

                // receive loop
                var buffer = new byte[8192];
                while (ws.State == System.Net.WebSockets.WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    System.Net.WebSockets.WebSocketReceiveResult res;
                    try
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // expected during shutdown/cancel
                        _logger?.LogDebug("[用户数据流] ReceiveAsync canceled due to token cancellation.");
                        break;
                    }
                    catch (System.Net.WebSockets.WebSocketException wex) when (ct.IsCancellationRequested)
                    {
                        // socket errors while we are canceling - treat as normal
                        _logger?.LogDebug(wex, "[用户数据流] WebSocket closed during cancellation.");
                        break;
                    }
                    catch (System.Net.WebSockets.WebSocketException wex)
                    {
                        // connection problem not caused by our cancellation - log and break to attempt reconnect
                        try { _logger?.LogWarning(wex, "[用户数据流] WebSocket receive failed, will reconnect."); } catch { }
                        break;
                    }
                    catch (Exception ex)
                    {
                        // unexpected error - log and break to attempt reconnect
                        try { _logger?.LogError(ex, "[用户数据流] Unexpected error while receiving WebSocket message."); } catch { }
                        break;
                    }

                    if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false); } catch { }
                        break;
                    }

                    var msg = System.Text.Encoding.UTF8.GetString(buffer, 0, res.Count);
                    try
                    {
                        using var dj = JsonDocument.Parse(msg);
                        var root = dj.RootElement;
                        if (root.TryGetProperty("e", out var et) && et.GetString() == "ORDER_TRADE_UPDATE")
                        {
                            if (root.TryGetProperty("o", out var o))
                            {
                                var execType = o.GetProperty("x").GetString();
                                if (execType == "TRADE")
                                {
                                    // extract fields
                                    var symbol = o.GetProperty("s").GetString() ?? string.Empty;
                                    var side = o.GetProperty("S").GetString() ?? string.Empty; // BUY/SELL
                                    var positionSide = o.GetProperty("ps").GetString() ?? string.Empty; // LONG/SHORT
                                    var price = decimal.Parse(o.GetProperty("L").GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                                    var qty = decimal.Parse(o.GetProperty("l").GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                                    decimal fee = 0m;
                                    string? feeAsset = null;
                                    if (o.TryGetProperty("N", out var n) && n.ValueKind != JsonValueKind.Null)
                                    {
                                        feeAsset = n.GetString();
                                    }
                                    if (o.TryGetProperty("n", out var nf) && nf.ValueKind != JsonValueKind.Null)
                                    {
                                        if (decimal.TryParse(nf.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fval)) fee = fval;
                                    }
                                    decimal rp = 0m;
                                    if (o.TryGetProperty("rp", out var rpv) && rpv.ValueKind != JsonValueKind.Null)
                                    {
                                        if (decimal.TryParse(rpv.GetRawText().Trim('"'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rv)) rp = rv;
                                    }
                                    var orderId = o.GetProperty("i").GetRawText().Trim('"');
                                    var tradeId = o.GetProperty("t").GetRawText().Trim('"');
                                    var eventTime = root.TryGetProperty("E", out var ev) ? DateTimeOffset.FromUnixTimeMilliseconds(ev.GetInt64()).UtcDateTime : DateTime.UtcNow;
                                    var isMaker = false;
                                    if (o.TryGetProperty("m", out var m) && m.ValueKind == JsonValueKind.True) isMaker = true;

                                    var args = new TradeFillEventArgs
                                    {
                                        Symbol = symbol,
                                        Side = side,
                                        PositionSide = positionSide,
                                        Quantity = qty,
                                        Price = price,
                                        Fee = fee,
                                        FeeAsset = feeAsset,
                                        RealizedPnl = rp,
                                        ExchangeOrderId = orderId,
                                        ExchangeTradeId = tradeId,
                                        Timestamp = eventTime,
                                        IsMaker = isMaker
                                    };

                                    try { TradeFilled?.Invoke(this, args); } catch { }
                                }
                            }
                        }
                        else if (root.TryGetProperty("e", out var eventType) && eventType.GetString() == "ACCOUNT_UPDATE")
                        {
                            // 转换并广播位置更新事件
                            ConcurrentBag<PositionUpdateEventArgs> updates = new ConcurrentBag<PositionUpdateEventArgs>();

                            if (root.TryGetProperty("p", out var positions) && positions.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var p in positions.EnumerateArray())
                                {
                                    try
                                    {
                                        // parse each position element
                                        var symbol = p.GetProperty("s").GetString() ?? string.Empty;
                                        var side = p.GetProperty("ps").GetString() ?? string.Empty; // LONG/SHORT
                                        var posAmt = p.GetProperty("pa").GetString() ?? "0";
                                        var entryPrice = p.GetProperty("ep").GetString() ?? "0";
                                        var markPrice = p.GetProperty("mp").GetString() ?? "0";

                                        if (decimal.TryParse(posAmt, out var amt) && amt != 0)
                                        {
                                            updates.Add(new PositionUpdateEventArgs
                                            {
                                                Symbol = symbol,
                                                Side = side,
                                                PositionAmt = amt,
                                                EntryPrice = decimal.Parse(entryPrice, CultureInfo.InvariantCulture),
                                                MarkPrice = decimal.Parse(markPrice, CultureInfo.InvariantCulture)
                                            });
                                        }
                                    }
                                    catch
                                    {
                                        // ignore malformed position entries
                                    }
                                }
                            }

                            // fire PositionChanged events
                            foreach (var update in updates)
                            {
                                try { PositionChanged?.Invoke(this, update); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                // stop keepalive
                try { keepaliveCts.Cancel(); } catch { }
                try { await keepaliveTask.ConfigureAwait(false); } catch { }

                try { _logger?.LogInformation("[币安] 用户数据流已断开，正在尝试重连"); } catch { }

                // attempt reconnect loop
                int attempt = 0;
                while (!ct.IsCancellationRequested && attempt < maxReconnectAttempts)
                {
                    attempt++;
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * attempt)), ct).ConfigureAwait(false);
                        break; // outer while will restart and create a new listenKey
                    }
                    catch (OperationCanceledException) { break; }
                }

                if (attempt >= maxReconnectAttempts)
                {
                    try { _logger?.LogWarning("[币安] 用户数据流重连尝试已达上限，停止重连"); } catch { }
                    // stop manager
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                try { _logger?.LogError($"[币安] 用户数据流错误: {ex.Message}, 正在尝试重连"); } catch { }
                // wait a bit before reconnecting
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); } catch { }
            }
        }

        try { _logger?.LogInformation("[币安] 用户数据流管理器退出"); } catch { }
    }

    private async Task<string?> CreateListenKeyAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/listenKey");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"StartUserDataStream failed: HTTP {resp.StatusCode} {txt}");
        }

        using var doc = JsonDocument.Parse(txt);
        if (!doc.RootElement.TryGetProperty("listenKey", out var lk))
            return null;

        return lk.GetString();
    }

    private async Task KeepAliveLoopAsync(string listenKey, CancellationToken ct)
    {
        // keep listenKey alive by PUT every 30 minutes
        var interval = TimeSpan.FromMinutes(30);
        if (interval <= TimeSpan.Zero) interval = TimeSpan.FromMinutes(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                var url = $"/fapi/v1/listenKey?listenKey={Uri.EscapeDataString(listenKey)}";
                using var req = new HttpRequestMessage(HttpMethod.Put, url);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    try { _logger?.LogTrace($"[币安] keepalive 成功 listenKey={listenKey}"); } catch { }
                }
                else
                {
                    try { _logger?.LogWarning($"[币安] keepalive 失败 listenKey={listenKey}: HTTP {resp.StatusCode} {txt}"); } catch { }
                    // treat as expired -> break so manager will recreate
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                try { _logger?.LogError($"[币安] keepalive 错误: {ex.Message}"); } catch { }
                // wait small amount and continue
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); } catch { }
            }
        }
    }

    public event EventHandler<TradeFillEventArgs>? TradeFilled;

    // Position update event args and event
    public sealed class PositionUpdateEventArgs : EventArgs
    {
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty; // LONG / SHORT
        public decimal PositionAmt { get; init; }
        public decimal EntryPrice { get; init; }
        public decimal MarkPrice { get; init; }
    }

    public event EventHandler<PositionUpdateEventArgs>? PositionChanged;

    /// <summary>
    /// 获取所有 symbol 的 positionRisk（fapi/v2/positionRisk 不带 symbol），返回 JsonDocument
    /// </summary>
    public async Task<JsonDocument?> GetAllPositionRiskAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException("调用签名接口前必须配置 ApiKey/ApiSecret。");

        using var req = CreateSignedGetRequest("/fapi/v2/positionRisk");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetAllPositionRisk failed: HTTP {resp.StatusCode} {txt}");
        }

        return JsonDocument.Parse(txt);
    }
}
