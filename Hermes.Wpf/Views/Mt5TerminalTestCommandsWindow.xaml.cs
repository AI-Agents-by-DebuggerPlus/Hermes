using System.Windows;
using System.Windows.Input;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class Mt5TerminalTestCommandsWindow : Window
{
    private readonly Func<string, Task> _sendToAgent;

    public Mt5TerminalTestCommandsWindow(Func<string, Task> sendToAgent)
    {
        InitializeComponent();
        _sendToAgent = sendToAgent ?? throw new ArgumentNullException(nameof(sendToAgent));
        CommandsList.ItemsSource = Mt5TerminalTestCommandCatalog.All;
        if (Mt5TerminalTestCommandCatalog.All.Count > 0)
        {
            CommandsList.SelectedIndex = 0;
        }
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e) => await SendAsync();

    private async void CommandsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CommandsList.SelectedItem != null)
        {
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        var custom = CustomPrompt.Text?.Trim() ?? string.Empty;
        string prompt;
        if (custom.Length > 0)
        {
            prompt = custom;
        }
        else if (CommandsList.SelectedItem is Mt5TerminalTestCommandCatalog.Item item)
        {
            prompt = item.Prompt;
        }
        else
        {
            StatusText.Text = "Выберите команду или введите свою.";
            StatusText.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        SendButton.IsEnabled = false;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x84, 0x8E, 0x9C));
        StatusText.Text = "Отправка в чат агента…";
        try
        {
            await _sendToAgent(prompt).ConfigureAwait(true);
            StatusText.Text = "Отправлено. Смотрите ответ агента в чате.";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x43, 0xA0, 0x47));
            CustomPrompt.Clear();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка: " + ex.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
