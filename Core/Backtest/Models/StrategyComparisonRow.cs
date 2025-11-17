using AiFuturesTerminal.Core.Strategy;

namespace AiFuturesTerminal.Core.Backtest.Models
{
    public sealed record StrategyComparisonRow(StrategyKind Strategy, BacktestSummary Summary)
    {
        public string StrategyName => Strategy.ToString();
        public decimal NetPnl => Summary.NetPnl;
        public decimal ProfitFactor => Summary.ProfitFactor;
        public double WinRate => Summary.WinRate;
        public int Trades => Summary.TradeCount;
        public decimal MaxDrawdown => Summary.MaxDrawdown;
    }
}
