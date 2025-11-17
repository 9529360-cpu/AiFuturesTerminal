namespace AiFuturesTerminal.Agent;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Strategy;
using AiFuturesTerminal.Core.Environment;
using Microsoft.Extensions.Logging;

/// <summary>
/// 简化版 Agent 服务实现，用于演示/本地测试。
/// 使用 StrategyConfig 提供参数化策略
/// </summary>
public sealed class AgentService : IAgentService
{
    private readonly StrategyConfig _config;
    private readonly ILogger<AgentService> _logger;
    private readonly ExecutionEngine _executionEngine;
    private readonly IStrategyFactory _strategyFactory;

    public AgentService(StrategyConfig config, ILogger<AgentService> logger, ExecutionEngine executionEngine, IStrategyFactory strategyFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    }

    private sealed class DayRiskState
    {
        public DateTime Date { get; set; }
        public int OpenTrades { get; set; }
        public int ConsecutiveLoses { get; set; }
        public DateTime? ScalpingOpenedAt { get; set; }
    }

    private readonly DayRiskState _dayState = new();

    /// <inheritdoc />
    public Task<AgentDecision> DecideAsync(AgentContext context, CancellationToken ct = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var kind = context.StrategyOverride ?? _config.Kind;

        return kind switch
        {
            StrategyKind.ScalpingMomentum => DecideScalpingAsync(context, ct),
            StrategyKind.TrendFollowing => DecideTrendFollowingAsync(context, ct),
            StrategyKind.RangeMeanReversion => DecideRangeAsync(context, ct),
            _ => DecideScalpingAsync(context, ct)
        };
    }

    private Task<AgentDecision> DecideScalpingAsync(AgentContext context, CancellationToken ct)
    {
        // reset daily state if date changed
        if (_dayState.Date.Date != context.Now.Date)
        {
            _dayState.Date = context.Now.Date;
            _dayState.OpenTrades = 0;
            _dayState.ConsecutiveLoses = 0;
            _dayState.ScalpingOpenedAt = null; // reset daily scalping entry time
        }

        var decision = ExecutionDecision.None(context.Symbol, strategyName: _config.Kind.ToString());
        var explanation = string.Empty;
        var strategy = $"ma_{_config.FastMaLength}_{_config.SlowMaLength}";
        decimal confidence = 0.5m;

        // require at least one candle for price information
        if (context.History == null || context.History.Count == 0)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: strategy),
                Explanation = "历史数据为空，无法决策。",
                StrategyName = strategy,
                Confidence = 0m
            });
        }

        // capture non-null history to help null analysis
        var history = context.History;
        var lastCandle = history[history.Count - 1];
        var lastPrice = lastCandle.Close;

        // If no position, consider opening based on simple MA momentum
        if (context.CurrentPosition == null || context.CurrentPosition.IsFlat())
        {
            // 回到空仓，清掉剥头皮进场时间
            _dayState.ScalpingOpenedAt = null;

            // wind down if daily limits hit
            if (_dayState.OpenTrades >= _config.MaxTradesPerDay)
            {
                return Task.FromResult(new AgentDecision
                {
                    ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: strategy),
                    Explanation = "已达到日内最大开仓次数限制，今日不再开新仓。",
                    StrategyName = strategy,
                    Confidence = 0m
                });
            }

            if (_dayState.ConsecutiveLoses >= _config.MaxConsecutiveLoses)
            {
                return Task.FromResult(new AgentDecision
                {
                    ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: strategy),
                    Explanation = "连续亏损次数过多，触发风控暂停开仓。",
                    StrategyName = strategy,
                    Confidence = 0m
                });
            }

            if (history.Count >= _config.SlowMaLength)
            {
                var fastMa = history.Skip(Math.Max(0, history.Count - _config.FastMaLength)).Average(h => h.Close);
                var slowMa = history.Skip(Math.Max(0, history.Count - _config.SlowMaLength)).Average(h => h.Close);

                if (fastMa > slowMa)
                {
                    decision = new ExecutionDecision { Type = ExecutionDecisionType.OpenLong, Symbol = context.Symbol, Reason = "ma_crossover", EntryPrice = lastPrice, LastPrice = lastPrice, StrategyName = strategy };
                    explanation = $"依据 MA({_config.FastMaLength}/{_config.SlowMaLength}) 金叉，建议开多。日内已开仓 {_dayState.OpenTrades}/{_config.MaxTradesPerDay}，连续亏损 {_dayState.ConsecutiveLoses}/{_config.MaxConsecutiveLoses}。";
                    confidence = 0.6m;
                }
                else if (fastMa < slowMa)
                {
                    decision = new ExecutionDecision { Type = ExecutionDecisionType.OpenShort, Symbol = context.Symbol, Reason = "ma_crossunder", EntryPrice = lastPrice, LastPrice = lastPrice, StrategyName = strategy };
                    explanation = $"依据 MA({_config.FastMaLength}/{_config.SlowMaLength}) 死叉，建议开空。日内已开仓 {_dayState.OpenTrades}/{_config.MaxTradesPerDay}，连续亏损 {_dayState.ConsecutiveLoses}/{_config.MaxConsecutiveLoses}。";
                    confidence = 0.6m;
                }
            }
        }
        else
        {
            // 有持仓：如果还没记录过进场时间，就在第一次看到持仓时记一次
            var pos = context.CurrentPosition;
            // defensive null check: if position unexpectedly null, log and return None safely
            if (pos == null)
            {
                try
                {
                    _logger.LogInformation("Scalping: current position is null in holding branch for Symbol={Symbol}", context.Symbol);
                }
                catch { }

                return Task.FromResult(new AgentDecision
                {
                    ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: strategy),
                    Explanation = "当前持仓信息缺失，无法执行回合判断",
                    StrategyName = strategy,
                    Confidence = 0m
                });
            }

            if (_dayState.ScalpingOpenedAt == null)
            {
                if (pos.EntryTime.HasValue)
                    _dayState.ScalpingOpenedAt = pos.EntryTime.Value;
                else
                    _dayState.ScalpingOpenedAt = context.Now;
            }

            // 有持仓时根据简单浮盈/浮亏以及配置的 R 倍数建议平仓

            // log entry into holding branch for scalping (kept but non-critical)
            try
            {
                _logger.LogInformation(
                    "Scalping 持仓检查：Symbol={Symbol}, Side={Side}, EntryTime={EntryTime:o}, Now={Now:o}",
                    context.Symbol,
                    pos.Side,
                    pos.EntryTime,
                    context.Now);
            }
            catch { /* logging should not throw */ }

            var pnl = pos.GetUnrealizedPnl(lastPrice);
            var pnlPercent = pos.EntryPrice == 0 ? 0m : pnl / (pos.EntryPrice * pos.Quantity);

            // map config multiples to simple thresholds for demo: StopLossRMultiple -> -1.5% * StopLossRMultiple
            var takeProfitThreshold = 0.01m * _config.TakeProfitRMultiple; // e.g., 0.02 if multiple 2
            var stopLossThreshold = -0.015m * _config.StopLossRMultiple;

            // compute held minutes using Agent's recorded open time
            var entryTime = _dayState.ScalpingOpenedAt ?? pos.EntryTime ?? context.Now;
            var heldMinutes = (context.Now - entryTime).TotalMinutes;
            var timeout = _config.ScalpingTimeoutMinutes;
            var info = $"Scalping 持仓时长={heldMinutes:F2} 分钟，超时阈值={timeout} 分钟";

            try
            {
                _logger.LogInformation(
                    "Scalping ??????={HeldMinutes:F2} ???????????={Timeout} ????",
                    heldMinutes,
                    timeout);
            }
            catch { }

            if (pnlPercent >= takeProfitThreshold)
            {
                decision = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "take_profit", LastPrice = lastPrice, StrategyName = strategy };
                explanation = $"当前浮盈达到 {takeProfitThreshold:P}，建议止盈平仓。 {info}";
                confidence = 0.7m;

                // update day state: simulate a winning trade (reset consecutive loses)
                _dayState.ConsecutiveLoses = 0;
            }
            else if (pnlPercent <= stopLossThreshold)
            {
                decision = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "stop_loss", LastPrice = lastPrice, StrategyName = strategy };
                explanation = $"当前浮亏达到 {stopLossThreshold:P}，建议止损平仓。 {info}";
                confidence = 0.9m;

                // simulate a loss
                _dayState.ConsecutiveLoses++;
            }
            else
            {
                // 时间强平逻辑（剥头皮策略）: 如果配置启用并且持仓超过阈值则强平
                if (pos != null)
                {
                    if (timeout > 0 && (heldMinutes >= timeout))
                    {
                        // log timeout trigger
                        try
                        {
                            _logger.LogInformation(
                                "Scalping 触发超时平仓：Symbol={Symbol}, Held={HeldMinutes:F2} 分钟 >= Timeout={Timeout}",
                                context.Symbol,
                                heldMinutes,
                                timeout);
                        }
                        catch { }

                        decision = new ExecutionDecision
                        {
                            Type = ExecutionDecisionType.Close,
                            Symbol = context.Symbol,
                            Side = pos.Side,
                            Reason = "剥头皮：超过超时时间自动平仓",
                            LastPrice = lastPrice,
                            StrategyName = strategy
                        };

                        explanation = $"Scalping 触发超时平仓：已持仓 {heldMinutes:F2} 分钟 >= 阈值 {timeout} 分钟。";
                        confidence = 0.5m;

                        // clear recorded scalping open time since we're closing
                        _dayState.ScalpingOpenedAt = null;
                    }
                    else
                    {
                        // not timed out: include holding info in explanation so UI logs show it
                        explanation = info;
                    }
                }
                else
                {
                    // no entry time available, still provide minimal info
                    explanation = info;
                }
            }
        }

        var result = new AgentDecision
        {
            ExecutionDecision = decision,
            Explanation = explanation,
            StrategyName = strategy,
            Confidence = confidence
        };

        // if we decided to open a trade, increment the day's open counter
        if (result.ExecutionDecision.Type == ExecutionDecisionType.OpenLong || result.ExecutionDecision.Type == ExecutionDecisionType.OpenShort)
        {
            _dayState.OpenTrades++;
            // when opening a scalping trade, record the open time if not already set
            if (_dayState.ScalpingOpenedAt == null)
            {
                // prefer decision.EntryPrice time if available? we use context.Now
                _dayState.ScalpingOpenedAt = context.Now;
            }
        }

        return Task.FromResult(result);
    }

    private Task<AgentDecision> DecideTrendFollowingAsync(AgentContext context, CancellationToken ct)
    {
        // reuse day risk checks
        if (_dayState.Date.Date != context.Now.Date)
        {
            _dayState.Date = context.Now.Date;
            _dayState.OpenTrades = 0;
            _dayState.ConsecutiveLoses = 0;
        }

        if (_dayState.OpenTrades >= _config.MaxTradesPerDay)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"),
                Explanation = "每日开仓次数达上限",
                StrategyName = "trend_following",
                Confidence = 0m
            });
        }

        if (_dayState.ConsecutiveLoses >= _config.MaxConsecutiveLoses)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"),
                Explanation = "连续亏损过多",
                StrategyName = "trend_following",
                Confidence = 0m
            });
        }

        var candles = context.History;
        if (candles == null || candles.Count < _config.TrendSlowMaLength)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"),
                Explanation = "数据不足",
                StrategyName = "trend_following",
                Confidence = 0m
            });
        }

        var latest = candles[^1];
        var close = latest.Close;

        var fastMa = SimpleMa(candles, _config.TrendFastMaLength);
        var slowMa = SimpleMa(candles, _config.TrendSlowMaLength);

        bool isBullTrend = fastMa > slowMa && close > slowMa;
        bool isBearTrend = fastMa < slowMa && close < slowMa;

        if (!isBullTrend && !isBearTrend)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"),
                Explanation = "无明显趋势",
                StrategyName = "trend_following",
                Confidence = 0m
            });
        }

        var pos = context.CurrentPosition;
        // 有持仓时处理止盈/止损/超时
        if (pos != null && !pos.IsFlat())
        {
            var lastPrice = close;
            var pnl = pos.GetUnrealizedPnl(lastPrice);
            var pnlPercent = pos.EntryPrice == 0 ? 0m : pnl / (pos.EntryPrice * pos.Quantity);

            var takeProfitThreshold = 0.01m * _config.TrendTakeProfitRMultiple;
            var stopLossThreshold = -0.015m * _config.TrendStopLossRMultiple;

            var heldMinutes = pos.EntryTime.HasValue ? (context.Now - pos.EntryTime.Value).TotalMinutes : 0.0;

            if (pnlPercent >= takeProfitThreshold)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "trend_take_profit", LastPrice = lastPrice, StrategyName = "trend_following" };
                _dayState.ConsecutiveLoses = 0;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止盈", StrategyName = "trend_following", Confidence = 0.8m });
            }

            if (pnlPercent <= stopLossThreshold)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "trend_stop_loss", LastPrice = lastPrice, StrategyName = "trend_following" };
                _dayState.ConsecutiveLoses++;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止损", StrategyName = "trend_following", Confidence = 0.9m });
            }

            if (heldMinutes >= _config.TrendMaxHoldingMinutes)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "trend_timeout_close", LastPrice = lastPrice, StrategyName = "trend_following" };
                var explanation = $"超时平仓 {heldMinutes:F2} >= {_config.TrendMaxHoldingMinutes}";
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = explanation, StrategyName = "trend_following", Confidence = 0.5m });
            }

            return Task.FromResult(new AgentDecision { ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"), Explanation = "持仓期间无动作", StrategyName = "trend_following", Confidence = 0.5m });
        }

        // 无持仓时，回调到快均线附近入场
        if (isBullTrend && close <= fastMa * 1.001m)
        {
            var openDec = new ExecutionDecision { Type = ExecutionDecisionType.OpenLong, Symbol = context.Symbol, Reason = "trend_pullback_long", EntryPrice = close, LastPrice = close, StrategyName = "trend_following" };
            _dayState.OpenTrades++;
            var explanation = $"开多: Fast={fastMa:F2}, Slow={slowMa:F2}";
            return Task.FromResult(new AgentDecision { ExecutionDecision = openDec, Explanation = explanation, StrategyName = "trend_following", Confidence = 0.7m });
        }

        if (isBearTrend && close >= fastMa * 0.999m)
        {
            var openDec = new ExecutionDecision { Type = ExecutionDecisionType.OpenShort, Symbol = context.Symbol, Reason = "trend_pullback_short", EntryPrice = close, LastPrice = close, StrategyName = "trend_following" };
            _dayState.OpenTrades++;
            var explanation = $"开空: Fast={fastMa:F2}, Slow={slowMa:F2}";
            return Task.FromResult(new AgentDecision { ExecutionDecision = openDec, Explanation = explanation, StrategyName = "trend_following", Confidence = 0.7m });
        }

        return Task.FromResult(new AgentDecision { ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "trend_following"), Explanation = "等待入场", StrategyName = "trend_following", Confidence = 0m });
    }

    private static decimal SimpleMa(IReadOnlyList<Candle> source, int length)
    {
        var count = source.Count;
        if (count == 0) return 0m;
        if (count < length) length = count;
        decimal sum = 0m;
        for (int i = count - length; i < count; i++)
            sum += source[i].Close;
        return sum / length;
    }

    private Task<AgentDecision> DecideRangeAsync(AgentContext context, CancellationToken ct)
    {
        // reuse day risk checks
        if (_dayState.Date.Date != context.Now.Date)
        {
            _dayState.Date = context.Now.Date;
            _dayState.OpenTrades = 0;
            _dayState.ConsecutiveLoses = 0;
        }

        if (_dayState.OpenTrades >= _config.MaxTradesPerDay)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"),
                Explanation = "每日开仓次数达上限",
                StrategyName = "range_mean_reversion",
                Confidence = 0m
            });
        }

        if (_dayState.ConsecutiveLoses >= _config.MaxConsecutiveLoses)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"),
                Explanation = "连续亏损过多",
                StrategyName = "range_mean_reversion",
                Confidence = 0m
            });
        }

        var candles = context.History;
        if (candles == null || candles.Count < _config.RangePeriod)
        {
            return Task.FromResult(new AgentDecision
            {
                ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"),
                Explanation = "数据不足",
                StrategyName = "range_mean_reversion",
                Confidence = 0m
            });
        }

        var (middle, upper, lower) = ComputeBollinger(candles, _config.RangePeriod, _config.RangeBandWidth);
        var latest = candles[^1];
        var close = latest.Close;

        var pos = context.CurrentPosition;
        if (pos == null || pos.IsFlat())
        {
            if (close >= upper * 0.999m)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.OpenShort, Symbol = context.Symbol, Reason = "range_upper_short", EntryPrice = close, LastPrice = close, StrategyName = "range_mean_reversion" };
                _dayState.OpenTrades++;
                var explanation = $"开空: Close={close:F2}, Upper={upper:F2}";
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = explanation, StrategyName = "range_mean_reversion", Confidence = 0.7m });
            }

            if (close <= lower * 1.001m)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.OpenLong, Symbol = context.Symbol, Reason = "range_lower_long", EntryPrice = close, LastPrice = close, StrategyName = "range_mean_reversion" };
                _dayState.OpenTrades++;
                var explanation = $"开多: Close={close:F2}, Lower={lower:F2}";
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = explanation, StrategyName = "range_mean_reversion", Confidence = 0.7m });
            }

            return Task.FromResult(new AgentDecision { ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"), Explanation = "等待入场", StrategyName = "range_mean_reversion", Confidence = 0m });
        }

        var lastPrice = close;
        var pnl = pos.GetUnrealizedPnl(lastPrice);
        var pnlPercent = pos.EntryPrice == 0 ? 0m : pnl / (pos.EntryPrice * pos.Quantity);

        // time held
        var heldMinutes = pos.EntryTime.HasValue ? (context.Now - pos.EntryTime.Value).TotalMinutes : 0.0;

        if (pos.Side == PositionSide.Long)
        {
            if (lastPrice >= middle)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_take_profit", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                _dayState.ConsecutiveLoses = 0;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止盈", StrategyName = "range_mean_reversion", Confidence = 0.8m });
            }

            if (lastPrice < lower)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_stop_loss", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                _dayState.ConsecutiveLoses++;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止损", StrategyName = "range_mean_reversion", Confidence = 0.9m });
            }

            if (heldMinutes >= _config.RangeMaxHoldingMinutes)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_timeout_close", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                var explanation = $"超时平仓 {heldMinutes:F2} >= {_config.RangeMaxHoldingMinutes}";
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = explanation, StrategyName = "range_mean_reversion", Confidence = 0.5m });
            }

            return Task.FromResult(new AgentDecision { ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"), Explanation = "持仓期间无动作", StrategyName = "range_mean_reversion", Confidence = 0.5m });
        }
        else
        {
            // short
            if (lastPrice <= middle)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_take_profit", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                _dayState.ConsecutiveLoses = 0;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止盈", StrategyName = "range_mean_reversion", Confidence = 0.8m });
            }

            if (lastPrice > upper)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_stop_loss", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                _dayState.ConsecutiveLoses++;
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = "止损", StrategyName = "range_mean_reversion", Confidence = 0.9m });
            }

            if (heldMinutes >= _config.RangeMaxHoldingMinutes)
            {
                var dec = new ExecutionDecision { Type = ExecutionDecisionType.Close, Symbol = context.Symbol, Reason = "range_timeout_close", LastPrice = lastPrice, StrategyName = "range_mean_reversion" };
                var explanation = $"超时平仓 {heldMinutes:F2} >= {_config.RangeMaxHoldingMinutes}";
                return Task.FromResult(new AgentDecision { ExecutionDecision = dec, Explanation = explanation, StrategyName = "range_mean_reversion", Confidence = 0.5m });
            }

            return Task.FromResult(new AgentDecision { ExecutionDecision = ExecutionDecision.None(context.Symbol, strategyName: "range_mean_reversion"), Explanation = "持仓期间无动作", StrategyName = "range_mean_reversion", Confidence = 0.5m });
        }
    }

    private static (decimal middle, decimal upper, decimal lower) ComputeBollinger(IReadOnlyList<Candle> candles, int period, decimal bandWidth)
    {
        var count = candles.Count;
        if (count == 0) return (0m, 0m, 0m);
        if (count < period) period = count;

        decimal sum = 0m;
        for (int i = count - period; i < count; i++) sum += candles[i].Close;
        var middle = sum / period;

        decimal variance = 0m;
        for (int i = count - period; i < count; i++)
        {
            var diff = candles[i].Close - middle;
            variance += diff * diff;
        }
        variance /= period;
        var sigma = (decimal)Math.Sqrt((double)variance);

        var upper = middle + bandWidth * sigma;
        var lower = middle - bandWidth * sigma;

        return (middle, upper, lower);
    }

    public async Task RunOnceAsync(ITradingEnvironment env, CancellationToken ct = default)
    {
        if (env == null) throw new ArgumentNullException(nameof(env));

        // Use StrategyConfigService? For now use the single injected StrategyConfig as active config
        // Fetch account snapshot once
        var account = await env.GetAccountSnapshotAsync(ct).ConfigureAwait(false);

        // For demo, use StrategyWatchConfig from MarketData? We'll iterate a small set: use one symbol BTCUSDT
        var symbols = new[] { "BTCUSDT" };

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();

            // choose lookback and interval based on strategy type
            var interval = TimeSpan.FromMinutes(1);
            var lookback = Math.Max(50, _config.RangePeriod);

            var candles = await env.GetRecentCandlesAsync(symbol, interval, lookback, ct).ConfigureAwait(false);
            if (candles == null || candles.Length == 0) continue;

            var pos = await env.GetOpenPositionAsync(symbol, ct).ConfigureAwait(false);

            var ctx = new StrategyContext
            {
                Now = env.UtcNow,
                CurrentBar = candles[^1],
                History = candles,
                CurrentPosition = pos,
                Account = account
            };

            // create strategy via factory? For simplicity, use DefaultStrategyFactory直接
            var factory = new DefaultStrategyFactory();
            var strat = factory.Create(_config);

            var decision = strat.OnBar(ctx);

            if (decision == null || decision.Type == ExecutionDecisionType.None) continue;

            // convert to ExecutionDecision and execute via engine using environment
            _logger.LogInformation($"Agent: executing decision {decision.Type} for {symbol}");

            await _executionEngine.ExecuteAsync(decision, env, ct).ConfigureAwait(false);
        }
    }
}
