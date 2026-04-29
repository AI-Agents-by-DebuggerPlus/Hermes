using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(HermesSettings settings)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(settings);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
