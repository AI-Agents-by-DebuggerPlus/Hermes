using System.Windows;
using System.Windows.Controls;
using Hermes.WpGallery.Tool.ViewModels;

namespace Hermes.WpGallery.Tool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    // ── Navigation ─────────────────────────────────────────
    private void NavCapture_Click(object sender, RoutedEventArgs e) => ShowTab(0);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowTab(1);
    private void NavLog_Click(object sender, RoutedEventArgs e)      => ShowTab(2);

    private void ShowTab(int idx)
    {
        TabCapture.Visibility  = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabSettings.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        TabLog.Visibility      = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Скрытие в трей без иконки делает окно «потерянным» — только обычная минимизация.
    protected override void OnStateChanged(EventArgs e) => base.OnStateChanged(e);

    // ── Close / cleanup ────────────────────────────────────
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SaveSettingsCommand.Execute(null);
            vm.Dispose();
        }
    }
}
