using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AiFuturesTerminal.UI.ViewModels;
using AiFuturesTerminal.UI.Views;

namespace AiFuturesTerminal
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly System.Func<StrategyConfigWindow> _strategyConfigWindowFactory;
        private readonly System.Func<TradeBookWindow> _tradeBookWindowFactory;

        public MainWindow(MainWindowViewModel viewModel, System.Func<StrategyConfigWindow> strategyConfigWindowFactory, System.Func<TradeBookWindow> tradeBookWindowFactory)
        {
            InitializeComponent();
            DataContext = viewModel;
            _strategyConfigWindowFactory = strategyConfigWindowFactory;
            _tradeBookWindowFactory = tradeBookWindowFactory;

            viewModel.OpenTradeBookRequested += (_, __) =>
            {
                var win = _tradeBookWindowFactory();
                win.Owner = this;
                win.Show();
            };

#if DEBUG
            // expose DEBUG-only clear history button
            try
            {
                ClearHistoryDbButton.Visibility = Visibility.Visible;
            }
            catch { }
#endif
        }

        private void OnOpenStrategyConfigClick(object sender, RoutedEventArgs e)
        {
            // create a fresh window instance each time; closed Window instances cannot be reused
            var win = _strategyConfigWindowFactory();
            win.Owner = this;
            win.ShowDialog();
        }

#if DEBUG
        private void OnClearHistoryDbClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.ClearHistoryDbCommand != null && vm.ClearHistoryDbCommand.CanExecute(null))
            {
                vm.ClearHistoryDbCommand.Execute(null);
            }
        }
#endif
    }
}