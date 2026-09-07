using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Hermes.EnglishTutorClient.Services;

namespace Hermes.EnglishTutorClient;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        AppLog.LineWritten += OnLine;
        AppLog.Cleared += OnCleared;
        Refresh();
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        AppLog.LineWritten -= OnLine;
        AppLog.Cleared -= OnCleared;
    }

    private void OnLine(string line)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }));
    }

    private void OnCleared()
    {
        Dispatcher.BeginInvoke(new Action(() => LogBox.Clear()));
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => Refresh();

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Очистить лог в окне и файл?\n" + AppLog.LogPath,
            "Очистить лог",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        AppLog.Clear();
        LogBox.Clear();
        PathText.Text = AppLog.LogPath;
    }

    private void Folder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogFolder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Лог", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh()
    {
        PathText.Text = AppLog.LogPath;
        try
        {
            LogBox.Text = File.Exists(AppLog.LogPath)
                ? File.ReadAllText(AppLog.LogPath)
                : "(пусто)";
            LogBox.ScrollToEnd();
        }
        catch (Exception ex)
        {
            LogBox.Text = ex.Message;
        }
    }
}
