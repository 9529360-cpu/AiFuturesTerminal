using System.Windows;
using AiFuturesTerminal.UI.ViewModels;

namespace AiFuturesTerminal.UI.Views
{
    public partial class AnalyticsWindow : Window
    {
        public AnalyticsWindow(AnalyticsViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
