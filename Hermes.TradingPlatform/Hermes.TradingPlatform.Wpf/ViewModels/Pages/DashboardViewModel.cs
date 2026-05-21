using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class DashboardViewModel : BaseViewModel
{
    public DashboardViewModel(MockTradingDataService data)
    {
        Account = data.GetAccountSummary();
        Pnl = data.GetPnlSummary();
        Risk = data.GetRiskStatus();
        Hermes = data.GetHermesStatus();
        foreach (var p in data.GetOpenPositions())
        {
            OpenPositions.Add(p);
        }

        foreach (var o in data.GetActiveOrders())
        {
            ActiveOrders.Add(o);
        }
    }

    public AccountSummaryDto Account { get; }
    public PnlSummaryDto Pnl { get; }
    public RiskStatusDto Risk { get; }
    public HermesStatusDto Hermes { get; }
    public ObservableCollection<PositionDto> OpenPositions { get; } = [];
    public ObservableCollection<OrderDto> ActiveOrders { get; } = [];
}
