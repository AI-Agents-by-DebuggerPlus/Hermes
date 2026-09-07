using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Hermes.Wpf.Views;

public partial class HermesReplyToastWindow : Window
{
    private readonly DispatcherTimer _autoClose;

    public HermesReplyToastWindow(string projectName, string preview, bool isError)
    {
        InitializeComponent();
        ProjectText.Text = string.IsNullOrWhiteSpace(projectName) ? "Hermes" : projectName;
        BodyText.Text = string.IsNullOrWhiteSpace(preview) ? "(пусто)" : preview;
        TitleText.Text = isError ? "Hermes — ошибка" : "Hermes ответил";
        TitleText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B6B")!)
            : new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F8D12F")!);

        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            try { Close(); } catch { /* ignore */ }
        };
        Loaded += (_, _) => _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var main = Application.Current?.MainWindow;
            if (main is not null)
            {
                if (main.WindowState == WindowState.Minimized)
                    main.WindowState = WindowState.Normal;
                main.Activate();
                main.Topmost = true;
                main.Topmost = false;
                main.Focus();
            }
        }
        catch
        {
            // ignore
        }

        Close();
    }
}
