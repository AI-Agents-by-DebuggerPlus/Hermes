using System.Windows;
using System.Windows.Controls;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(Action<string, string> applyCredentials)
    {
        InitializeComponent();
        _vm = new SettingsViewModel(applyCredentials);
        DataContext = _vm;
        SecretBox.Password = _vm.SecretKey;
    }

    private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _vm.SecretKey = box.Password;
        }
    }

    private void OnSaveApiKeys(object sender, RoutedEventArgs e) => _vm.SaveApiKeysCommand.Execute(null);
    private void OnSaveRisk(object sender, RoutedEventArgs e) => _vm.SaveRiskCommand.Execute(null);
    private void OnClearKeys(object sender, RoutedEventArgs e)
    {
        _vm.ClearKeysCommand.Execute(null);
        SecretBox.Password = string.Empty;
    }
}
