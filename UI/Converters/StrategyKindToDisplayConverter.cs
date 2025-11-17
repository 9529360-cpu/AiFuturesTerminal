namespace AiFuturesTerminal.UI.Converters;

using System;
using System.Globalization;
using System.Windows.Data;
using AiFuturesTerminal.Core.Strategy;

public sealed class StrategyKindToDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StrategyKind kind)
        {
            return kind switch
            {
                StrategyKind.ScalpingMomentum => "剥头皮动量",
                StrategyKind.TrendFollowing => "趋势跟随",
                StrategyKind.RangeMeanReversion => "区间均值回归",
                _ => kind.ToString()
            };
        }

        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "全部";
            if (Enum.TryParse<StrategyKind>(s, true, out var parsed))
            {
                return parsed switch
                {
                    StrategyKind.ScalpingMomentum => "剥头皮动量",
                    StrategyKind.TrendFollowing => "趋势跟随",
                    StrategyKind.RangeMeanReversion => "区间均值回归",
                    _ => s
                };
            }
            return s; // custom strategy name, return as-is
        }

        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 不支持双向转换
        return Binding.DoNothing;
    }
}
