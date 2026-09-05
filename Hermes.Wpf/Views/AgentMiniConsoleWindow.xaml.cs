using System.Windows;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class AgentMiniConsoleWindow : Window
{
    private readonly AgentActivityBus _bus;
    private readonly Func<string?> _workspace;

    public AgentMiniConsoleWindow(AgentActivityBus bus, Func<string?> workspace)
    {
        InitializeComponent();
        _bus = bus;
        _workspace = workspace;
        _bus.Changed += OnChanged;
        Closed += (_, _) => _bus.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.Invoke(Refresh);

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var ws = _workspace() ?? "(none)";
        TitleLabel.Text = $"Mini Console — {ws}";
        Title = $"Mini Console — {ws}";
        LogList.ItemsSource = _bus.Snapshot(ws, 300).Select(e => e.DisplayLine).Reverse().ToList();
    }
}
