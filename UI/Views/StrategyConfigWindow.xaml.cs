namespace AiFuturesTerminal.UI.Views;

using System.Windows;
using AiFuturesTerminal.UI.ViewModels;

public partial class StrategyConfigWindow : Window
{
    public StrategyConfigWindow(StrategyConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += StrategyConfigWindow_Loaded;
    }

    private void StrategyConfigWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // set Chinese labels at runtime to avoid XAML encoding issues
        TbRiskLabel.Text = "单笔风险占比（0.01 = 1%）：";
        TbMaxTradesLabel.Text = "单日最大开仓次数：";
        TbMaxConsecLabel.Text = "允许连续亏损次数：";
        BtnSave.Content = "保存";
        BtnClose.Content = "关闭";
        Title = "策略参数设置";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is StrategyConfigViewModel vm)
        {
            vm.Save();
        }

        DialogResult = true;
    }
}