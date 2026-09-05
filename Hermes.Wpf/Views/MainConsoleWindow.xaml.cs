using System.Windows;
using System.Windows.Controls;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class MainConsoleWindow : Window
{
    private readonly AgentActivityBus _bus;

    public MainConsoleWindow(AgentActivityBus bus)
    {
        InitializeComponent();
        _bus = bus;
        _bus.Changed += OnChanged;
        Closed += (_, _) => _bus.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.Invoke(Refresh);

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _bus.Clear();
        Refresh();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var filter = FilterBox.Text?.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            filter = null;
        }

        LogList.ItemsSource = _bus.Snapshot(filter, 800).Select(e => e.DisplayLine).Reverse().ToList();
    }
}
