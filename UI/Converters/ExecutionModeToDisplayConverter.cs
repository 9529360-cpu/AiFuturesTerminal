namespace AiFuturesTerminal.UI.Converters;

using System;
using System.Globalization;
using System.Windows.Data;
using AiFuturesTerminal.Core.Execution;

public sealed class ExecutionModeToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ExecutionMode mode)
            return "未知模式";

        return mode switch
        {
            ExecutionMode.DryRun => "仿真（DryRun，仅记录不下单）",
            ExecutionMode.Testnet => "测试网下单（Testnet）",
            _ => "未知模式",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
