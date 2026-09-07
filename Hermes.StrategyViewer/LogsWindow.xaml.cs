using System.Diagnostics;
using System.Windows;
using Hermes.StrategyViewer.Services;

namespace Hermes.StrategyViewer;

public partial class LogsWindow : Window
{
    private readonly StrategyLogService _log;

    public LogsWindow(StrategyLogService log)
    {
        InitializeComponent();
        _log = log;
        PathLabel.Text = _log.LogFilePath;
        _log.LineAppended += OnLineAppended;
        RefreshLog();
    }

    public void RefreshLog()
    {
        LogBox.Text = _log.ReadTail();
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
    }

    private void OnLineAppended(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLog();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _log.LogDirectory,
            UseShellExecute = true,
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _log.LineAppended -= OnLineAppended;
        base.OnClosed(e);
    }
}
