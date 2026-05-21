using System.Windows;
using System.Windows.Controls;
using Hermes.TradingPlatform.Wpf.ViewModels;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages;
using Hermes.TradingPlatform.Wpf.Views.Pages;

namespace Hermes.TradingPlatform.Wpf.Navigation;

public static class ViewLocator
{
    public static UserControl? Resolve(BaseViewModel? viewModel) => viewModel switch
    {
        DashboardViewModel => new DashboardView(),
        PositionsViewModel => new PositionsView(),
        OrdersViewModel => new OrdersView(),
        StrategiesViewModel => new StrategiesView(),
        RiskManagerViewModel => new RiskManagerView(),
        MarketWatchViewModel => new MarketWatchView(),
        ReplayViewModel => new ReplayView(),
        JournalViewModel => new JournalView(),
        LogsViewModel => new LogsView(),
        HermesViewModel => new HermesView(),
        SettingsViewModel => new SettingsView(),
        _ => null,
    };

    public static FrameworkElement? CreateContent(BaseViewModel? viewModel)
    {
        var view = Resolve(viewModel);
        if (view is null)
        {
            return null;
        }

        view.DataContext = viewModel;
        return view;
    }
}
