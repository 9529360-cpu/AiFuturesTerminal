namespace AiFuturesTerminal.Core.Execution;

using System;
using System.Threading;
using System.Threading.Tasks;
using AiFuturesTerminal.Core.Exchanges;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Analytics;
using AiFuturesTerminal.Core.Risk;
using AiFuturesTerminal.Core.Environment;
using Microsoft.Extensions.Logging;

/// <summary>
/// 执行引擎：用于回测/Agent/实盘的统一执行路径。
/// 已扩展支持 ITradingEnvironment 路径。
/// </summary>
public sealed class ExecutionEngine
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IRiskEngine _riskEngine;
    private readonly AppEnvironmentOptions _envOptions;
    private readonly StrategyConfig _strategyConfig;
    private readonly ITradeBook _tradeBook;
    private readonly IGlobalRiskGuard _globalRiskGuard;
    private readonly GlobalRiskCoordinator _riskCoordinator;
    private readonly ILogger<ExecutionEngine>? _logger;

    /// <summary>
    /// DryRun ???????????????????д?? UI ??????
    /// </summary>
    public event EventHandler<string>? DryRunPlanned;

    /// <summary>
    /// Testnet / Live ??????????????? UI ????????н????????????? string-based event for compatibility.
    /// </summary>
    public event EventHandler<string>? ExecutionInfo;

    /// <summary>
    /// ?????????? ExecutionInfo ????????? Kind/Symbol/Message/Time
    /// </summary>
    public event EventHandler<ExecutionInfo>? ExecutionInfoStructured;

    /// <summary>
    /// ??????????档
    /// </summary>
    public ExecutionEngine(IExchangeAdapter exchangeAdapter, IRiskEngine riskEngine, AppEnvironmentOptions envOptions, StrategyConfig strategyConfig, ITradeBook tradeBook, IGlobalRiskGuard globalRiskGuard, GlobalRiskCoordinator riskCoordinator, ILogger<ExecutionEngine>? logger = null)
    {
        _exchangeAdapter = exchangeAdapter ?? throw new ArgumentNullException(nameof(exchangeAdapter));
        _riskEngine = riskEngine ?? throw new ArgumentNullException(nameof(riskEngine));
        _envOptions = envOptions ?? throw new ArgumentNullException(nameof(envOptions));
        _strategyConfig = strategyConfig ?? throw new ArgumentNullException(nameof(strategyConfig));
        _tradeBook = tradeBook ?? throw new ArgumentNullException(nameof(tradeBook));
        _globalRiskGuard = globalRiskGuard ?? throw new ArgumentNullException(nameof(globalRiskGuard));
        _riskCoordinator = riskCoordinator ?? throw new ArgumentNullException(nameof(riskCoordinator));
        _logger = logger;
    }

    private void RaiseExecutionInfo(string message)
    {
        ExecutionInfo?.Invoke(this, message);
    }

    private void RaiseExecutionInfo(ExecutionInfoKind kind, string symbol, string message)
    {
        var payload = new ExecutionInfo(kind, symbol, message, DateTime.UtcNow);

        // keep the structured payload message unchanged so internal logic can rely on machine codes
        // For legacy string consumers / UI we map known machine messages to Chinese display text.
        static string MapMessageToDisplay(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return string.Empty;

            return msg switch
            {
                "placed_open_long" => "已下单 开多",
                "placed_open_short" => "已下单 开空",
                "placed_open" => "已下单",
                "placed_close" => "已下单 平仓",
                "closed" => "已下单 平仓",
                "opened_long" => "已下单 开多",
                "opened_short" => "已下单 开空",
                "opened" => "已下单",
                "Order routed to exchange; will record on real fill" => "已将订单发送到交易所（实盘/Testnet），等待真实成交后再记录到交易簿",
                _ => msg
            };
        }

        // map some kinds to Chinese short labels for legacy string consumers
        var kindLabel = kind switch
        {
            ExecutionInfoKind.OrderPlaced => "委托",
            ExecutionInfoKind.OrderClosed => "委托",
            ExecutionInfoKind.RiskBlocked => "风险拦截",
            ExecutionInfoKind.Error => "错误",
            ExecutionInfoKind.Warning => "警告",
            ExecutionInfoKind.DryRun => "DryRun",
            _ => kind.ToString()
        };

        var displayMessage = MapMessageToDisplay(message);
        var serialized = $"[{kindLabel}] {symbol}：{displayMessage}";

        // invoke legacy string event with localized display message
        ExecutionInfo?.Invoke(this, serialized);

        // invoke structured event with original payload (machine message preserved)
        ExecutionInfoStructured?.Invoke(this, payload);

        // also mirror to DryRunPlanned for visibility where appropriate
        DryRunPlanned?.Invoke(this, serialized);
    }

    /// <summary>
    /// ????????з????????? ITradingEnvironment??????? RiskEngine / OrderRouter / TradeBook
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(ExecutionDecision decision, ITradingEnvironment env, CancellationToken ct = default)
    {
        if (decision == null) throw new ArgumentNullException(nameof(decision));
        if (env == null) throw new ArgumentNullException(nameof(env));

        // Defensive guards for nullable analysis: ensure collaborators are present
        if (_strategyConfig == null) throw new InvalidOperationException("StrategyConfig is not initialized in ExecutionEngine.");
        if (_globalRiskGuard == null) throw new InvalidOperationException("GlobalRiskGuard is not initialized in ExecutionEngine.");
        if (_riskCoordinator == null) throw new InvalidOperationException("GlobalRiskCoordinator is not initialized in ExecutionEngine.");
        if (env.RiskEngine == null) throw new InvalidOperationException("Trading environment RiskEngine is null.");
        if (env.OrderRouter == null) throw new InvalidOperationException("Trading environment OrderRouter is null.");

        if (decision.Type == ExecutionDecisionType.None) return new ExecutionResult { Success = true, Message = "none", Symbol = decision.Symbol };

        // get account & position from environment
        var account = await env.GetAccountSnapshotAsync(ct).ConfigureAwait(false);
        var position = await env.GetOpenPositionAsync(decision.Symbol, ct).ConfigureAwait(false);

        // take a defensive snapshot of the current position so downstream adapters cannot mutate it
        Position? positionSnapshot = null;
        if (position != null)
        {
            // shallow copy of key fields used for PnL calculation
            positionSnapshot = new Position(position.Symbol)
            {
                Side = position.Side,
                Quantity = position.Quantity,
                EntryPrice = position.EntryPrice,
                EntryTime = position.EntryTime
            };
        }

        // apply risk rules using environment's risk engine
        var finalDecision = env.RiskEngine.ApplyRiskRules(account, position, decision);

        // Additional rule for Testnet/Live: ensure exchange has no existing position before allowing open
        if (_envOptions.ExecutionMode == ExecutionMode.Testnet || _envOptions.ExecutionMode == ExecutionMode.Live)
        {
            try
            {
                if (_riskEngine is AiFuturesTerminal.Core.Execution.BasicRiskEngine bre)
                {
                    finalDecision = await bre.RejectIfExchangeHasPositionAsync(new InternalExecutionEnvironment(_exchangeAdapter, _riskEngine, _tradeBook, this), finalDecision).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while applying exchange-position based risk checks");
            }
        }

        if (finalDecision.Type == ExecutionDecisionType.None)
        {
            return new ExecutionResult { Success = false, Message = "risk_rejected", Symbol = decision.Symbol };
        }

        // enrich decision with existing strategy config (reuse this._strategyConfig)
        EnrichDecisionWithRisk(finalDecision, account, position);

        // check global guard using this engine's _globalRiskGuard
        if (finalDecision.Type == ExecutionDecisionType.OpenLong || finalDecision.Type == ExecutionDecisionType.OpenShort)
        {
            var globalSettings = new GlobalRiskSettings
            {
                RiskPerTrade = _strategyConfig.RiskPerTrade,
                MaxTradesPerDay = _strategyConfig.MaxTradesPerDay,
                MaxConsecutiveLoss = _strategyConfig.MaxConsecutiveLoses
            };

            var runtime = _riskCoordinator.Runtime;
            var gdec = _globalRiskGuard.CanOpenNewPosition(globalSettings, runtime);
            if (!gdec.IsAllowed)
            {
                var msg = gdec.Reason ?? "global_guard_block";
                RaiseExecutionInfo(ExecutionInfoKind.RiskBlocked, finalDecision.Symbol ?? string.Empty, msg);
                return new ExecutionResult { Success = false, Message = "global_guard_block", Symbol = finalDecision.Symbol };
            }
        }

        // Route order via environment's OrderRouter
        var execResult = await env.OrderRouter.RouteAsync(finalDecision, ct).ConfigureAwait(false);

        // After routing, if it's a close or successful open, record trade into env.TradeBook
        if (execResult.Success && finalDecision.Type == ExecutionDecisionType.Close)
        {
            // Only simulate / record trades locally for Backtest and DryRun modes.
            // For Testnet/Live we must wait for actual exchange fills and let the adapter/router record the trade.
            if (_envOptions.ExecutionMode == ExecutionMode.Backtest || _envOptions.ExecutionMode == ExecutionMode.DryRun)
            {
                await RecordTradeAsync(env.TradeBook, positionSnapshot, finalDecision, _envOptions.ExecutionMode).ConfigureAwait(false);
            }
            else
            {
                // In Testnet/Live, do not write fake trades here. Notify UI that order was placed and real fill should be processed by adapter/router.
                RaiseExecutionInfo(ExecutionInfoKind.OrderPlaced, finalDecision.Symbol ?? string.Empty, "已将订单发送到交易所（实盘/Testnet），等待真实成交后再记录到交易簿");
            }
        }

        // raise structured events
        if (execResult.Success)
        {
            RaiseExecutionInfo(ExecutionInfoKind.OrderPlaced, finalDecision.Symbol ?? string.Empty, execResult.Message);
        }
        else
        {
            RaiseExecutionInfo(ExecutionInfoKind.Error, finalDecision.Symbol ?? string.Empty, execResult.Message);
        }

        return execResult;
    }

    // Legacy compatibility wrapper: build an internal environment that uses this engine's adapters
    public Task ExecuteAsync(ExecutionDecision decision, CancellationToken ct = default)
    {
        var env = new InternalExecutionEnvironment(_exchangeAdapter, _riskEngine, _tradeBook, this);
        // fire and forget the result but return task
        return ExecuteAsync(decision, env, ct);
    }

    private sealed class InternalExecutionEnvironment : ITradingEnvironment
    {
        private readonly IExchangeAdapter _adapter;
        private readonly IRiskEngine _riskEngine;
        private readonly ITradeBook _tradeBook;
        private readonly IOrderRouter _router;

        public InternalExecutionEnvironment(IExchangeAdapter adapter, IRiskEngine riskEngine, ITradeBook tradeBook, ExecutionEngine engine)
        {
            _adapter = adapter;
            _riskEngine = riskEngine;
            _tradeBook = tradeBook;
            _router = new AdapterOrderRouter(adapter);
        }

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public ITradeBook TradeBook => _tradeBook;
        public IRiskEngine RiskEngine => _riskEngine;
        public IOrderRouter OrderRouter => _router;

        public Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct = default) => _adapter.GetAccountSnapshotAsync(ct);
        public Task<Position?> GetOpenPositionAsync(string symbol, CancellationToken ct = default) => _adapter.GetOpenPositionAsync(symbol, ct);
        public async Task<Candle[]> GetRecentCandlesAsync(string symbol, TimeSpan interval, int limit, CancellationToken ct = default)
            => (await _adapter.GetHistoricalCandlesAsync(symbol, interval, limit, ct).ConfigureAwait(false))?.ToArray() ?? Array.Empty<Candle>();

        private sealed class AdapterOrderRouter : IOrderRouter
        {
            private readonly IExchangeAdapter _adapter;

            public AdapterOrderRouter(IExchangeAdapter adapter)
            {
                _adapter = adapter;
            }

            public async Task<ExecutionResult> RouteAsync(ExecutionDecision decision, CancellationToken cancellationToken = default)
            {
                try
                {
                    switch (decision.Type)
                    {
                        case ExecutionDecisionType.OpenLong:
                            // ensure adapter sees the intended entry price if provided (helps MockExchangeAdapter)
                            if (decision.EntryPrice.HasValue && _adapter is AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter mock)
                            {
                                try { mock.SetLastPrice(decision.Symbol, decision.EntryPrice.Value); } catch { }
                            }
                            await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Long, decision.Quantity ?? 1m, decision.Reason, cancellationToken).ConfigureAwait(false);
                            return new ExecutionResult { Success = true, Message = "已下单 开多", Symbol = decision.Symbol };
                        case ExecutionDecisionType.OpenShort:
                            if (decision.EntryPrice.HasValue && _adapter is AiFuturesTerminal.Core.Exchanges.Mock.MockExchangeAdapter mock2)
                            {
                                try { mock2.SetLastPrice(decision.Symbol, decision.EntryPrice.Value); } catch { }
                            }
                            await _adapter.PlaceOrderAsync(decision.Symbol, PositionSide.Short, decision.Quantity ?? 1m, decision.Reason, cancellationToken).ConfigureAwait(false);
                            return new ExecutionResult { Success = true, Message = "已下单 开空", Symbol = decision.Symbol };
                        case ExecutionDecisionType.Close:
                            await _adapter.ClosePositionAsync(decision.Symbol, decision.Reason, cancellationToken).ConfigureAwait(false);
                            return new ExecutionResult { Success = true, Message = "已下单 平仓", Symbol = decision.Symbol };
                        default:
                            return new ExecutionResult { Success = false, Message = "none", Symbol = decision.Symbol };
                    }
                }
                catch (Exception ex)
                {
                    return new ExecutionResult { Success = false, Message = ex.Message, Symbol = decision.Symbol };
                }
            }
        }
    }

    // Record trade helper used for both DryRun and Testnet after a successful close
    private Task RecordTradeAsync(ITradeBook tradeBook, Position? position, ExecutionDecision decision, ExecutionMode mode)
    {
        // Be tolerant: only skip if no position or zero quantity
        if (position == null || position.Quantity == 0m)
        {
            RaiseExecutionInfo("[交易簿] 无持仓可记录");
            return Task.CompletedTask;
        }

        var symbol = decision.Symbol ?? position.Symbol ?? string.Empty;
        var qty = Math.Abs(position.Quantity);
        var side = position.Side;

        // Prefer entry price from the existing position snapshot (the "old" avg price).
        // Fallback to decision-provided prices only if position entry price is not available.
        decimal entryPrice = position.EntryPrice > 0m ? position.EntryPrice : (decision.EntryPrice ?? decision.LastPrice ?? 0m);

        if (entryPrice <= 0m)
        {
            RaiseExecutionInfo("[交易簿] 无效的开仓价，PnL=0");
        }

        decimal exitPrice;
        if (decision.LastPrice.HasValue)
        {
            exitPrice = decision.LastPrice.Value;
        }
        else if (decision.EntryPrice.HasValue)
        {
            exitPrice = decision.EntryPrice.Value;
        }
        else if (position != null && position.EntryPrice > 0m)
        {
            // fallback: if adapter didn't provide a last price, use position entry (will yield zero pnl)
            exitPrice = position.EntryPrice;
        }
        else
        {
            exitPrice = entryPrice;
        }

        var openTime = position.EntryTime ?? DateTime.UtcNow;
        var closeTime = DateTime.UtcNow;

        // Prepare inputs for PnL calculation and log them for debugging
        try
        {
            _logger?.LogDebug("[盈亏调试] symbol={Symbol}, side={Side}, qty={Qty}, entry={Entry}, exit_candidate={ExitCandidate}", symbol, side, qty, entryPrice, decision.LastPrice ?? decision.EntryPrice ?? (position?.EntryPrice ?? (decimal?)null));
        }
        catch { }

        // Use Binance USDT-M futures PnL formula
        decimal rawPnl = 0m;
        decimal fee = 0m;
        decimal effectiveExitPrice = exitPrice;

        if (entryPrice > 0m && exitPrice > 0m && qty > 0m)
        {
            // apply slippage only in DryRun/Backtest modes
            if (mode == ExecutionMode.DryRun || mode == ExecutionMode.Testnet)
            {
                var direction = side == PositionSide.Long ? 1m : -1m;
                var slip = _envOptions.SlippageTicksPerTrade * _envOptions.SlippageTickSize; // price offset
                effectiveExitPrice = exitPrice + direction * slip;

                // recalc raw pnl using effective fill price
                rawPnl = BinancePnlCalculator.CalculateRealizedPnlUsdM(side, entryPrice, effectiveExitPrice, qty);

                // compute fee: treat backtest orders as taker by default
                var feeRate = _envOptions.TakerFeeRate;
                var notional = effectiveExitPrice * qty;
                fee = notional * feeRate;

                // include fee in pnl
                rawPnl += fee;
            }
            else
            {
                // live mode: use actual exitPrice and fee will be zero here (adapter should record real fee)
                rawPnl = BinancePnlCalculator.CalculateRealizedPnlUsdM(side, entryPrice, exitPrice, qty);
                fee = 0m;
            }
        }

        // Round PnL to sensible precision
        var pnl = decimal.Round(rawPnl, 8);

        try
        {
            _logger?.LogDebug("[盈亏调试] symbol={Symbol}, entry={Entry}, exit={Exit}, effectiveExit={EffectiveExit}, qty={Qty}, rawPnl={RawPnl}, roundedPnl={Pnl}, fee={Fee}", symbol, entryPrice, exitPrice, effectiveExitPrice, qty, rawPnl, pnl, fee);
        }
        catch { }

        // Sanity clamp: avoid astronomical values from mis-sized quantities/prices
        const decimal maxAbsPnl = 1_000_000_000m; // 1 billion USDT per trade cap
        if (pnl > maxAbsPnl)
        {
            _logger?.LogWarning("[交易簿] 盈亏过大 ({Pnl})，限制为 {Max}", pnl, maxAbsPnl);
            pnl = maxAbsPnl;
        }
        else if (pnl < -maxAbsPnl)
        {
            _logger?.LogWarning("[交易簿] 盈亏过小 ({Pnl})，限制为 {Min}", pnl, -maxAbsPnl);
            pnl = -maxAbsPnl;
        }

        var tradeSide = side == PositionSide.Long ? TradeSide.Long : TradeSide.Short;

        // ensure StrategyName reflects decision's strategy identity when available
        string strategyName = decision.StrategyName ?? decision.Reason ?? "Agent";

        var trade = new TradeRecord
        {
            OpenTime = openTime,
            CloseTime = closeTime,
            Symbol = symbol,
            Side = tradeSide,
            Quantity = qty,
            EntryPrice = entryPrice,
            ExitPrice = effectiveExitPrice,
            RealizedPnl = pnl,
            Fee = fee,
            StrategyName = strategyName,
            Mode = mode
        };

        // Log trade strategy name and pnl for diagnostics
        try
        {
            _logger?.LogInformation("[成交] {Strategy} {Symbol} 盈亏={Pnl:F4}", strategyName, symbol, pnl);
        }
        catch { }

        // write to configured tradebook asynchronously if it supports async
        _ = Task.Run(async () =>
        {
            try
            {
                if (tradeBook != null)
                {
                    await tradeBook.AddAsync(trade).ConfigureAwait(false);
                }

                // Also mirror to application-wide persistent book if different
                try
                {
                    var global = _tradeBook;
                    if (global != null && !ReferenceEquals(global, tradeBook))
                    {
                        await global.AddAsync(trade).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error mirroring trade to global tradebook");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error writing trade to tradebook");
            }
        });

        return Task.CompletedTask;
    }

    // enrich decision with existing strategy config (reuse this._strategyConfig)
    private void EnrichDecisionWithRisk(ExecutionDecision decision, AccountSnapshot account, Position? position)
    {
        // copied from old implementation; trimming for brevity
        if (decision.Type != ExecutionDecisionType.OpenLong &&
            decision.Type != ExecutionDecisionType.OpenShort)
            return;

        var equity = account?.Equity ?? 0m;
        if (equity <= 0)
        {
            RaiseExecutionInfo("[风控] 账户权益为 0");
            return;
        }

        // Use RiskPerTradePct from strategy config (fraction of equity)
        var riskPerTradePct = _strategyConfig != null ? _strategyConfig.RiskPerTradePct : 0.01m;
        if (riskPerTradePct <= 0m) riskPerTradePct = 0.01m;

        var riskAmount = equity * riskPerTradePct; // in USDT
        if (riskAmount <= 0m)
        {
            RaiseExecutionInfo("[风控] 风险金额无效");
            return;
        }

        // Determine entry price (use decision preferred price, fallback to position)
        var entryPrice = decision.EntryPrice ?? decision.LastPrice ?? position?.EntryPrice ?? 0m;
        if (entryPrice <= 0m)
        {
            RaiseExecutionInfo("[风控] 开仓价为 0，无法计算仓位");
            return;
        }

        // Determine stop loss distance in USDT per unit (prefer explicit stop loss price if provided)
        decimal stopLossDistance = 0m;
        if (decision.StopLossPrice.HasValue && decision.StopLossPrice.Value > 0m)
        {
            stopLossDistance = Math.Abs(entryPrice - decision.StopLossPrice.Value);
        }

        // Compute quantity in asset units (e.g., BTC) based on risk amount and stop loss distance (USDT)
        decimal rawQty = 0m;
        if (stopLossDistance > 0m)
        {
            rawQty = riskAmount / stopLossDistance; // units of asset
        }
        else
        {
            // no stop provided or zero distance: fallback conservative fixed qty
            rawQty = Math.Max( (_strategyConfig?.MinQtyStep ?? 0.001m), 0.01m );
            _logger?.LogWarning("[风控] 未提供止损 for {Symbol}, 回退数量={Qty}", decision.Symbol, rawQty);
        }

        // apply symbol-specific step size (use config MinQtyStep)
        var symbol = decision.Symbol ?? string.Empty;
        var step = (_strategyConfig?.MinQtyStep ?? 0.001m);
        if (step <= 0m) step = 0.001m;

        var floored = Math.Floor(rawQty / step) * step;

        // enforce minimum
        if (floored < step)
        {
            _logger?.LogWarning("[风控] floored qty {Floored} < step {Step}, using step as qty", floored, step);
            floored = step;
        }

        // clamp to configured max qty
        var maxQty = (_strategyConfig?.MaxQty ?? 10m);
        if (floored > maxQty)
        {
            _logger?.LogWarning("[风控] 数量过大: {Qty}, 限制为 {Max}", floored, maxQty);
            floored = maxQty;
        }

        // compute notional in USDT and clamp by MaxNotional
        var notional = floored * entryPrice;
        var maxNotional = (_strategyConfig?.MaxNotional ?? 10_000m);
        if (notional > maxNotional)
        {
            _logger?.LogWarning("[风控] 名义额过大: {Notional}, 限制仓位以匹配 {MaxNotional}", notional, maxNotional);
            floored = Math.Floor((maxNotional / entryPrice) / step) * step;
            notional = floored * entryPrice;
        }

        // final rounding and assignment
        decision.Quantity = decimal.Round(floored, 6);
        decision.Notional = decimal.Round(notional, 2);

        // diagnostic log for unusually large values
        if (decision.Quantity > 1m || decision.Notional > 5000m)
        {
            _logger?.LogInformation("[风控] 决策尺寸: Symbol={Symbol}, Entry={Entry}, Stop={Stop}, Qty={Qty}, Notional={Notional}", decision.Symbol, entryPrice, decision.StopLossPrice, decision.Quantity, decision.Notional);
        }
    }
}
