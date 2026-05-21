using System.Windows;
using Hermes.TradingPlatform.Wpf.ViewModels.Shell;

namespace Hermes.TradingPlatform.Wpf.Views.Shell;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}
