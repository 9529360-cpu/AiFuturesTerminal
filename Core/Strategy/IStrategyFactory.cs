using AiFuturesTerminal.Core.Backtest;
using System;
using AiFuturesTerminal.Core.Models;
using AiFuturesTerminal.Core.Execution;

namespace AiFuturesTerminal.Core.Strategy
{
    /// <summary>
    /// 策略工厂，负责根据配置创建 IStrategy 实例
    /// </summary>
    public interface IStrategyFactory
    {
        /// <summary>
        /// 创建策略实例，策略类型由 config.Kind 决定
        /// </summary>
        /// <param name="config">策略配置</param>
        /// <returns>IStrategy 实例</returns>
        IStrategy Create(StrategyConfig config);
    }
}
