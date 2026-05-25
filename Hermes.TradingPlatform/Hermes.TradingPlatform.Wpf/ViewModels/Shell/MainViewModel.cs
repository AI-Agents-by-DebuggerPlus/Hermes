using Hermes.InAppAssistant;
using Hermes.InAppAssistant.Wpf;
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

        var assistantContext = new TradingInAppAssistantContextProvider(() => this);
        InAppAssistant = new MiniAssistantViewModel(
            new AppAssistantService(logger: new TradingAppAssistantLogger()),
            () =>
            {
                var s = _host.PlatformSettingsStore.Load();
                return new AppAssistantOptions
                {
                    ApplicationId = AppAssistantKnowledge.TradingPlatformId,
                    OpenRouterApiKey = s.InAppAssistantOpenRouterApiKey,
                    Model = s.InAppAssistantOpenRouterModel,
                };
            },
            assistantContext);

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
            [NavigationPage.Logs] = new LogsViewModel(readModel, _host),
            [NavigationPage.Hermes] = new HermesViewModel(readModel),
            [NavigationPage.AccountSettings] = new AccountSettingsViewModel(_host),
            [NavigationPage.Assistant] = new AssistantViewModel(_host, InAppAssistant),
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
            new NavItemViewModel(NavigationPage.Dashboard, "Dashboard", "⌂", "Обзор счёта и статуса платформы"),
            new NavItemViewModel(NavigationPage.Positions, "Positions", "◎", "Открытые позиции бумажного счёта"),
            new NavItemViewModel(NavigationPage.Orders, "Orders", "⇄", "Активные и исполненные ордера"),
            new NavItemViewModel(NavigationPage.Strategies, "Strategies", "◈", "Торговые стратегии и их состояние"),
            new NavItemViewModel(NavigationPage.RiskManager, "Risk Manager", "⚠", "Лимиты риска, риск на сделку %, аварийная остановка"),
            new NavItemViewModel(NavigationPage.MarketWatch, "Market Watch", "◉", "Котировки и быстрые действия по инструментам"),
            new NavItemViewModel(NavigationPage.Replay, "Replay", "▶", "Воспроизведение исторических данных"),
            new NavItemViewModel(NavigationPage.Journal, "Journal", "📓", "Журнал сделок"),
            new NavItemViewModel(NavigationPage.Logs, "Logs", "☰", "Системные логи терминала"),
            new NavItemViewModel(NavigationPage.Hermes, "Hermes", "✦", "Монитор оркестрации Hermes (не LLM-чат)"),
            new NavItemViewModel(NavigationPage.AccountSettings, "Account", "$", "Баланс сброса, кредитное плечо, сброс paper-счёта"),
            new NavItemViewModel(NavigationPage.Assistant, "Assistant", "◆", "OpenRouter: ключ, модель и чат с ИИ"),
            new NavItemViewModel(NavigationPage.Settings, "Settings", "⚙", "Данные рынка, UI, интеграция Hermes"),
        ];

        NavigateCommand = new RelayCommand(p => Navigate(p is NavigationPage page ? page : NavigationPage.Dashboard));
        Navigate(NavigationPage.Dashboard);

        SupabaseStartupNotifier.TryPublishOnStartup();
    }

    public TradingPlatformHost Host => _host;

    public MiniAssistantViewModel InAppAssistant { get; }

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    private NavigationPage _activeNavPage = NavigationPage.Dashboard;
    private BaseViewModel? _currentPage;

    public BaseViewModel? CurrentPage
    {
        get => _currentPage;
        private set => SetField(ref _currentPage, value);
    }

    public bool IsAssistantTab => _activeNavPage == NavigationPage.Assistant;

    public bool ShowFloatingAssistant => _activeNavPage != NavigationPage.Assistant;

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

        var leavingAssistant = _activeNavPage == NavigationPage.Assistant;
        _activeNavPage = page;
        CurrentPage = vm;
        PageTitle = NavItems.First(n => n.Page == page).Title;
        foreach (var item in NavItems)
        {
            item.IsSelected = item.Page == page;
        }

        Raise(nameof(IsAssistantTab));
        Raise(nameof(ShowFloatingAssistant));

        if (page == NavigationPage.Assistant)
        {
            InAppAssistant.IsOpen = true;
        }
        else if (leavingAssistant)
        {
            InAppAssistant.IsOpen = false;
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
