using System;
using System.Text;
using System.Windows;
using Hermes.EnglishLearning.Services;

namespace Hermes.EnglishLearning;

public partial class LogWindow : Window
{
    private bool _closingByUser = true;

    public LogWindow()
    {
        InitializeComponent();
        PathText.Text = "Файл: " + AppLog.CurrentLogPath + " (сессия = файл, очищается при перезапуске)";
        ReloadFromAppLog();
        AppLog.LineAdded += OnLine;
        Closed += (_, __) => AppLog.LineAdded -= OnLine;
        IsVisibleChanged += (_, __) =>
        {
            if (IsVisible)
            {
                ReloadFromAppLog();
            }
        };
    }

    public void ReloadFromAppLog()
    {
        var sb = new StringBuilder();
        foreach (var line in AppLog.GetSessionLines())
        {
            sb.AppendLine(line);
        }

        LogBox.Text = sb.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
        PathText.Text = "Файл: " + AppLog.CurrentLogPath + " · строк: " + AppLog.GetSessionLines().Count;
    }

    private void OnLine(string line)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }));
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        // UI only — file stays as session truth until restart
        LogBox.Clear();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        _closingByUser = true;
        Hide();
    }

    private void Window_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingByUser)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ForceClose()
    {
        _closingByUser = false;
        Close();
    }
}
