using System.Collections.ObjectModel;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class SettingsViewModel : BaseViewModel
{
    public SettingsViewModel()
    {
        ExchangeModes = new ObservableCollection<string>(
        [
            "Virtual (Paper)",
            "Binance Futures (Phase 4)",
        ]);
    }

    public ObservableCollection<string> ExchangeModes { get; }

    private string _exchangeMode = "Virtual (Paper)";

    public string ExchangeMode
    {
        get => _exchangeMode;
        set => SetField(ref _exchangeMode, value);
    }

    private string _apiEndpoint = "(not connected — Phase 4)";

    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set => SetField(ref _apiEndpoint, value);
    }

    private string _theme = "Dark Trading";

    public string Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    private string _defaultLeverageText = "3";

    public string DefaultLeverageText
    {
        get => _defaultLeverageText;
        set => SetField(ref _defaultLeverageText, value);
    }

    private string _replayDataPath = "%AppData%\\HermesTrading\\Replay";

    public string ReplayDataPath
    {
        get => _replayDataPath;
        set => SetField(ref _replayDataPath, value);
    }

    private bool _hermesIntegrationEnabled;

    public bool HermesIntegrationEnabled
    {
        get => _hermesIntegrationEnabled;
        set => SetField(ref _hermesIntegrationEnabled, value);
    }
}
