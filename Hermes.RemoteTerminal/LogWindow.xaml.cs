using System.ComponentModel;
using System.Text;
using System.Windows;
using Hermes.RemoteTerminal.Services;

namespace Hermes.RemoteTerminal;

public partial class LogWindow : Window
{
    private bool _hideOnClose = true;

    public event Func<string, Task>? SendToSupabaseRequested;

    public LogWindow()
    {
        InitializeComponent();
        Title = "Лог — Remote Terminal " + AppVersion.Display;
        PathText.Text = "Файл: " + AppLog.LogPath;
        Reload();
        AppLog.LineAdded += OnLine;
        Closed += (_, _) => AppLog.LineAdded -= OnLine;
    }

    public void ForceClose()
    {
        _hideOnClose = false;
        Close();
    }

    private void OnLine(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void Reload()
    {
        var sb = new StringBuilder();
        foreach (var line in AppLog.GetSessionLines())
        {
            sb.AppendLine(line);
        }

        LogBox.Text = sb.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
    }

    private async void SendSupabase_OnClick(object sender, RoutedEventArgs e)
    {
        var lines = AppLog.GetSessionLines();
        if (lines.Count == 0)
        {
            MessageBox.Show(this, "Нет строк лога для отправки.", "Лог", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Cap payload size for PostgREST.
        const int maxChars = 12000;
        var take = Math.Min(lines.Count, 200);
        var chunk = lines.Skip(Math.Max(0, lines.Count - take));
        var body = string.Join("\n", chunk);
        if (body.Length > maxChars)
        {
            body = body[^maxChars..];
        }

        var payload = "[LOG:RemoteTerminal]\n" + body;
        if (SendToSupabaseRequested is null)
        {
            MessageBox.Show(this, "Обработчик отправки не подключён.", "Лог", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await SendToSupabaseRequested(payload).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Лог → Supabase", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_hideOnClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
