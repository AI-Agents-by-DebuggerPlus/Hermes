using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Data.Persistence;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Threading;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly TradingPlatformHost _host;

    public SettingsViewModel(TradingPlatformHost host)
    {
        _host = host;
        MarketDataModes =
        [
            PlatformSettingsFileStore.ToDisplayName(MarketDataSource.Mock),
            PlatformSettingsFileStore.ToDisplayName(MarketDataSource.BinanceFutures),
        ];

        ExchangeModes =
        [
            "Virtual (Paper)",
            "Binance Futures (execution Phase 4+)",
        ];

        MarketDataMode = PlatformSettingsFileStore.ToDisplayName(_host.MarketDataSource);
        ApiEndpoint = _host.MarketDataEndpoint;
        ExchangeMode = "Virtual (Paper)";
        HermesIntegrationEnabled = _host.HermesOrchestrationEnabled;
        TradingSoundsEnabled = _host.PlatformSettingsStore.Load().TradingSoundsEnabled;

        ApplyMarketDataCommand = new RelayCommand(_ => ApplyMarketDataMode());
        _host.FeedStatusChanged += (_, _) => WpfThreading.RunOnUi(RefreshFeedStatus);
        RefreshFeedStatus();
    }

    public ObservableCollection<string> MarketDataModes { get; }
    public ObservableCollection<string> ExchangeModes { get; }

    private string _marketDataMode = "";

    public string MarketDataMode
    {
        get => _marketDataMode;
        set => SetField(ref _marketDataMode, value);
    }

    private string _feedStatus = "";

    public string FeedStatus
    {
        get => _feedStatus;
        private set => SetField(ref _feedStatus, value);
    }

    private string _exchangeMode = "Virtual (Paper)";

    public string ExchangeMode
    {
        get => _exchangeMode;
        set => SetField(ref _exchangeMode, value);
    }

    private string _apiEndpoint = "";

    public string ApiEndpoint
    {
        get => _apiEndpoint;
        private set => SetField(ref _apiEndpoint, value);
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
        set
        {
            if (SetField(ref _hermesIntegrationEnabled, value))
            {
                _host.SetHermesOrchestrationEnabled(value);
            }
        }
    }

    private bool _tradingSoundsEnabled = true;

    public bool TradingSoundsEnabled
    {
        get => _tradingSoundsEnabled;
        set
        {
            if (SetField(ref _tradingSoundsEnabled, value))
            {
                _host.SetTradingSoundsEnabled(value);
            }
        }
    }

    public RelayCommand ApplyMarketDataCommand { get; }

    private void ApplyMarketDataMode()
    {
        var source = MarketDataMode.Contains("Binance", StringComparison.OrdinalIgnoreCase)
            ? MarketDataSource.BinanceFutures
            : MarketDataSource.Mock;

        _host.SetMarketDataSource(source);
        ApiEndpoint = _host.MarketDataEndpoint;
        RefreshFeedStatus();
    }

    private void RefreshFeedStatus() =>
        FeedStatus = $"{_host.ActiveFeed?.Name ?? "—"} · {_host.FeedStatusLabel}";
}
