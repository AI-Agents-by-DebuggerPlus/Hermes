using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopVoiceChat.Services;

namespace DesktopVoiceChat;

public partial class LogWindow : Window
{
    private static LogWindow? _instance;

    public LogWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnWindowClosed;
    }

    public static void ShowOrActivate(Window? owner = null)
    {
        if (_instance is { } existing)
        {
            existing.Activate();
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            return;
        }

        var window = new LogWindow { Owner = owner };
        _instance = window;
        window.Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppLogService.MessageLogged -= OnMessageLogged;
        AppLogService.MessageLogged += OnMessageLogged;

        var path = AppLogService.CurrentLogFilePath;
        if (path is not null && File.Exists(path))
        {
            try
            {
                LogTextBox.Text = File.ReadAllText(path);
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
            }
            catch
            {
                // ignore read errors; live logging still works
            }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        AppLogService.MessageLogged -= OnMessageLogged;
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void OnMessageLogged(object? sender, string line)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (LogTextBox.Text.Length > 0)
            {
                LogTextBox.AppendText(Environment.NewLine);
            }

            LogTextBox.AppendText(line);
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
            LogTextBox.ScrollToEnd();
        });
    }

    private void OnClearViewClick(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(AppLogService.LogDirectory))
            {
                Directory.CreateDirectory(AppLogService.LogDirectory);
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = AppLogService.LogDirectory,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Журнал", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
