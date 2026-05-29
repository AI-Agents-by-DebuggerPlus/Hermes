using System.Windows;
using System.Windows.Controls;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Wpf.Services;
using Hermes.SpotTerminal.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.SpotTerminal.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _vm;
        LoadApiSettingsIntoUi();
        InitTestPositionUi();
    }

    private void OnVirtual(object sender, RoutedEventArgs e) => _vm.SetModeVirtual();
    private void OnSpotDemo(object sender, RoutedEventArgs e) => _vm.SetModeSpotDemo();
    private void OnAgentThought(object sender, RoutedEventArgs e) => _vm.AgentThoughtDemo();
    private void OnTestLong(object sender, RoutedEventArgs e) => _ = RunTestOrderAsync(SpotOrderSide.Buy);
    private void OnTestShort(object sender, RoutedEventArgs e) => _ = RunTestOrderAsync(SpotOrderSide.Sell);

    private void OnTestQuick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag)
        {
            return;
        }

        var parts = tag.Split('|');
        if (parts.Length != 2)
        {
            return;
        }

        TestSymbolCombo.Text = parts[0];
        var side = parts[1] == "Buy" ? SpotOrderSide.Buy : SpotOrderSide.Sell;
        _ = RunTestOrderAsync(side, parts[0]);
    }
    private void OnBacktest(object sender, RoutedEventArgs e) => _vm.BacktestFirstSkill();
    private void OnLogsAll(object sender, RoutedEventArgs e) => _vm.LogFilter = "All";
    private void OnLogsAgent(object sender, RoutedEventArgs e) => _vm.LogFilter = "Agent";

    private void OnReloadApiSettings(object sender, RoutedEventArgs e) => LoadApiSettingsIntoUi();

    private void OnSaveApiSettings(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyTextBox.Text?.Trim() ?? "";
        var apiSecret = ApiSecretBox.Password?.Trim() ?? "";
        _vm.SaveBinanceApiKeys(apiKey, apiSecret);
    }

    private void LoadApiSettingsIntoUi()
    {
        var s = _vm.LoadPlatformSettings();
        ApiKeyTextBox.Text = s.BinanceApiKey ?? "";
        ApiSecretBox.Password = s.BinanceApiSecret ?? "";
    }

    private void InitTestPositionUi()
    {
        var symbols = _vm.LoadPlatformSettings().WatchSymbols?.ToList()
                      ?? ["BTCUSDT", "ETHUSDT", "SOLUSDT"];
        TestSymbolCombo.ItemsSource = symbols;
        TestSymbolCombo.Text = symbols.FirstOrDefault() ?? "BTCUSDT";
        LogFilePathText.Text = "Log: " + SpotTerminalFileLogger.Instance.SessionPath;
    }

    private async Task RunTestOrderAsync(SpotOrderSide side, string? symbol = null)
    {
        symbol ??= TestSymbolCombo.Text?.Trim() ?? "BTCUSDT";
        if (!MainViewModel.TryParseQuantity(TestQtyTextBox.Text, out var qty))
        {
            TestOrderStatusText.Text = "Некорректный объём.";
            TestOrderStatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        TestOrderStatusText.Text = "Отправка…";
        TestOrderStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        var msg = await _vm.PlaceTestMarketAsync(side, symbol, qty).ConfigureAwait(true);
        TestOrderStatusText.Text = msg;
        TestOrderStatusText.Foreground = msg.Contains("Filled", StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Media.Brushes.LightGreen
            : msg.Contains("Rejected", StringComparison.OrdinalIgnoreCase) || msg.Contains("Ошибка", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Brushes.OrangeRed
                : System.Windows.Media.Brushes.White;
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
