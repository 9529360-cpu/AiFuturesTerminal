using System.Windows;
using AiFuturesTerminal.UI.ViewModels;

namespace AiFuturesTerminal.UI.Views
{
    public partial class BacktestWindow : Window
    {
        public BacktestWindow()
        {
            InitializeComponent();
        }

        public void Initialize(BacktestViewModel vm)
        {
            DataContext = vm;
        }
    }
}
