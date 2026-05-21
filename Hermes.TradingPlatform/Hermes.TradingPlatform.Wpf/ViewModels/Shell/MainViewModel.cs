using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Navigation;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Shell;

public sealed class MainViewModel : BaseViewModel
{
    private readonly MockTradingDataService _data = new();
    private readonly Dictionary<NavigationPage, BaseViewModel> _pages;

    public MainViewModel()
    {
        _pages = new Dictionary<NavigationPage, BaseViewModel>
        {
            [NavigationPage.Dashboard] = new DashboardViewModel(_data),
            [NavigationPage.Positions] = new PositionsViewModel(_data),
            [NavigationPage.Orders] = new OrdersViewModel(_data),
            [NavigationPage.Strategies] = new StrategiesViewModel(_data),
            [NavigationPage.RiskManager] = new RiskManagerViewModel(_data),
            [NavigationPage.MarketWatch] = new MarketWatchViewModel(_data),
            [NavigationPage.Replay] = new ReplayViewModel(),
            [NavigationPage.Logs] = new LogsViewModel(_data),
            [NavigationPage.Hermes] = new HermesViewModel(_data),
            [NavigationPage.Settings] = new SettingsViewModel(),
        };

        NavItems =
        [
            new NavItemViewModel(NavigationPage.Dashboard, "Dashboard", "⌂"),
            new NavItemViewModel(NavigationPage.Positions, "Positions", "◎"),
            new NavItemViewModel(NavigationPage.Orders, "Orders", "⇄"),
            new NavItemViewModel(NavigationPage.Strategies, "Strategies", "◈"),
            new NavItemViewModel(NavigationPage.RiskManager, "Risk Manager", "⚠"),
            new NavItemViewModel(NavigationPage.MarketWatch, "Market Watch", "◉"),
            new NavItemViewModel(NavigationPage.Replay, "Replay", "▶"),
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

    public string TopBarSubtitle { get; private set; } = "Paper Trading · Virtual Exchange · Phase 1 UI";

    public string ConnectionStatus { get; } = "SIMULATION";
    public string SessionClock { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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
}
