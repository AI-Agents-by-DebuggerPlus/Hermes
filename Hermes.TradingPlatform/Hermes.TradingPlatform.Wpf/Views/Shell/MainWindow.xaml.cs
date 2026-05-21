using System.Windows;
using Hermes.TradingPlatform.Wpf.ViewModels.Shell;

namespace Hermes.TradingPlatform.Wpf.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
