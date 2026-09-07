using System.Windows;
using Hermes.StrategyViewer.Services;
using Hermes.StrategyViewer.ViewModels;
using Microsoft.Win32;

namespace Hermes.StrategyViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly StrategyLogService _log;
    private readonly Action _openLogs;

    public MainWindow(MainViewModel vm, StrategyLogService log, Action openLogs)
    {
        InitializeComponent();
        _vm = vm;
        _log = log;
        _openLogs = openLogs;
        DataContext = _vm;
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Strategy JSON|*.json|All|*.*",
            Title = "Open strategy JSON",
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _vm.LoadStrategy(dlg.FileName);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _vm.RefreshMarketAsync();

    private void Logs_Click(object sender, RoutedEventArgs e) => _openLogs();
}
