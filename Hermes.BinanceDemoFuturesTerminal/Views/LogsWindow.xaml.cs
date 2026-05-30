using System.Diagnostics;
using System.Windows;
using Hermes.BinanceDemoFuturesTerminal.Services;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Views;

public partial class LogsWindow : Window
{
    private readonly LogsViewModel _vm = new();

    public LogsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        AppServices.Log.LineAdded += OnLineAdded;
        Closed += (_, _) => AppServices.Log.LineAdded -= OnLineAdded;
    }

    private void OnLineAdded(string _)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[0]);
            }
        });
    }

    private void OnClear(object sender, RoutedEventArgs e) => _vm.ClearViewCommand.Execute(null);
    private void OnOpenFolder(object sender, RoutedEventArgs e) => _vm.OpenFolderCommand.Execute(null);
    private void OnCopyPath(object sender, RoutedEventArgs e) => _vm.CopyPathCommand.Execute(null);
}
