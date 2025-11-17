using System;
using AiFuturesTerminal.Core.Execution;
using AiFuturesTerminal.Core.Models;

namespace AiFuturesTerminal.Core.Strategy
{
    public sealed class DefaultStrategyFactory : IStrategyFactory
    {
        public IStrategy Create(StrategyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            return config.Kind switch
            {
                StrategyKind.ScalpingMomentum => new ScalpingMomentumStrategy(config),
                StrategyKind.TrendFollowing => new TrendFollowingStrategy(config),
                StrategyKind.RangeMeanReversion => new RangeMeanReversionStrategy(config),
                _ => throw new NotSupportedException($"Unsupported strategy kind: {config.Kind}")
            };
        }
    }
}
