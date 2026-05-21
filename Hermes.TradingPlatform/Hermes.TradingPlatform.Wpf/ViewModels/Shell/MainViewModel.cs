using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Navigation;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Threading;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Shell;

public sealed class MainViewModel : BaseViewModel, IDisposable
{
    private readonly TradingPlatformHost _host = new();
    private readonly Bridge.TradingBridgePublisher _bridgePublisher;
    private readonly Bridge.TradingBridgeCommandProcessor _bridgeCommands;
    private readonly Dictionary<NavigationPage, BaseViewModel> _pages;

    public MainViewModel()
    {
        TradingPlatformFileLogger.Instance.Info($"log file: {TradingPlatformFileLogger.Instance.SessionPath}");
        TradingPlatformFileLogger.Instance.Info($"session state: {_host.SessionStateStore.FilePath}");
        TradingPlatformFileLogger.Instance.Info($"trade journal: {_host.JournalFileWriter.FilePath}");
        _bridgePublisher = new Bridge.TradingBridgePublisher(_host);
        _bridgeCommands = new Bridge.TradingBridgeCommandProcessor(_host);
        _host.Start();
        var readModel = _host.ReadModel;

        _pages = new Dictionary<NavigationPage, BaseViewModel>
        {
            [NavigationPage.Dashboard] = new DashboardViewModel(readModel),
            [NavigationPage.Positions] = new PositionsViewModel(readModel, _host.Exchange),
            [NavigationPage.Orders] = new OrdersViewModel(readModel, _host.Exchange),
            [NavigationPage.Strategies] = new StrategiesViewModel(readModel, _host),
            [NavigationPage.RiskManager] = new RiskManagerViewModel(readModel, _host),
            [NavigationPage.MarketWatch] = new MarketWatchViewModel(readModel, _host.Exchange),
            [NavigationPage.Replay] = new ReplayViewModel(),
            [NavigationPage.Journal] = new JournalViewModel(readModel),
            [NavigationPage.Logs] = new LogsViewModel(readModel),
            [NavigationPage.Hermes] = new HermesViewModel(readModel),
            [NavigationPage.Settings] = new SettingsViewModel(_host),
        };

        _host.FeedStatusChanged += (_, _) => WpfThreading.RunOnUi(UpdateConnectionStatus);
        readModel.StateChanged += (_, _) => WpfThreading.RunOnUi(RefreshAccountSummary);
        TradeUiFeedback.Instance.MessageChanged += (_, _) =>
            WpfThreading.RunOnUi(() => TradeStatusLine = TradeUiFeedback.Instance.LastMessage);
        UpdateConnectionStatus();
        RefreshAccountSummary();
        TradeStatusLine = TradeUiFeedback.Instance.LastMessage;

        NavItems =
        [
            new NavItemViewModel(NavigationPage.Dashboard, "Dashboard", "⌂"),
            new NavItemViewModel(NavigationPage.Positions, "Positions", "◎"),
            new NavItemViewModel(NavigationPage.Orders, "Orders", "⇄"),
            new NavItemViewModel(NavigationPage.Strategies, "Strategies", "◈"),
            new NavItemViewModel(NavigationPage.RiskManager, "Risk Manager", "⚠"),
            new NavItemViewModel(NavigationPage.MarketWatch, "Market Watch", "◉"),
            new NavItemViewModel(NavigationPage.Replay, "Replay", "▶"),
            new NavItemViewModel(NavigationPage.Journal, "Journal", "📓"),
            new NavItemViewModel(NavigationPage.Logs, "Logs", "☰"),
            new NavItemViewModel(NavigationPage.Hermes, "Hermes", "✦"),
            new NavItemViewModel(NavigationPage.Settings, "Settings", "⚙"),
        ];

        NavigateCommand = new RelayCommand(p => Navigate(p is NavigationPage page ? page : NavigationPage.Dashboard));
        Navigate(NavigationPage.Dashboard);
    }

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    private BaseViewModel? _currentPage;

    public BaseViewModel? CurrentPage
    {
        get => _currentPage;
        private set => SetField(ref _currentPage, value);
    }

    private string _pageTitle = "Dashboard";

    public string PageTitle
    {
        get => _pageTitle;
        private set => SetField(ref _pageTitle, value);
    }

    public string TopBarSubtitle { get; private set; } = "Paper Trading · Virtual Exchange · Phase 6";

    private string _connectionStatus = "SIMULATION";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string SessionClock { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private AccountSummaryDto _account = new();

    public AccountSummaryDto Account
    {
        get => _account;
        private set => SetField(ref _account, value);
    }

    private PnlSummaryDto _pnl = new();

    public PnlSummaryDto Pnl
    {
        get => _pnl;
        private set => SetField(ref _pnl, value);
    }

    private int _openPositionsCount;

    public int OpenPositionsCount
    {
        get => _openPositionsCount;
        private set => SetField(ref _openPositionsCount, value);
    }

    private string _tradeStatusLine = string.Empty;

    public string TradeStatusLine
    {
        get => _tradeStatusLine;
        private set => SetField(ref _tradeStatusLine, value);
    }

    public RelayCommand NavigateCommand { get; }

    public void Navigate(NavigationPage page)
    {
        if (!_pages.TryGetValue(page, out var vm))
        {
            return;
        }

        CurrentPage = vm;
        PageTitle = NavItems.First(n => n.Page == page).Title;
        foreach (var item in NavItems)
        {
            item.IsSelected = item.Page == page;
        }
    }

    private void UpdateConnectionStatus() => ConnectionStatus = _host.FeedStatusLabel;

    private void RefreshAccountSummary()
    {
        var readModel = _host.ReadModel;
        Account = readModel.GetAccountSummary();
        Pnl = readModel.GetPnlSummary();
        OpenPositionsCount = readModel.GetOpenPositions().Count;
    }

    public void Dispose()
    {
        _bridgeCommands.Dispose();
        _bridgePublisher.Dispose();
        _host.Dispose();
    }
}
