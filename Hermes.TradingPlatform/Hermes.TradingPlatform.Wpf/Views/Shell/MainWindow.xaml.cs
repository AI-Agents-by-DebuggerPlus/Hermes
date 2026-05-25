using System.Windows;
using Hermes.TradingPlatform.Wpf.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.TradingPlatform.Wpf.Views.Shell;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}
