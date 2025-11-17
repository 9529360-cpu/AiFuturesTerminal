using System.Windows;
using AiFuturesTerminal.UI.ViewModels;

namespace AiFuturesTerminal.UI.Views;

public partial class TradeBookWindow : Window
{
    public TradeBookWindow(TradeBookViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
