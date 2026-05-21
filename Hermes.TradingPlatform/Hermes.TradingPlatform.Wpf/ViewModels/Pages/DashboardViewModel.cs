using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class DashboardViewModel : TradingPageViewModel
{
    public DashboardViewModel(TradingReadModel readModel)
        : base(readModel) => Refresh();

    private AccountSummaryDto _account = new();
    private PnlSummaryDto _pnl = new();
    private RiskStatusDto _risk = new() { RiskLevel = "Low" };
    private HermesStatusDto _hermes = new()
    {
        State = "Monitoring",
        ActiveStrategy = "",
        Mode = "Paper / Simulation",
    };

    public AccountSummaryDto Account
    {
        get => _account;
        private set => SetField(ref _account, value);
    }

    public PnlSummaryDto Pnl
    {
        get => _pnl;
        private set => SetField(ref _pnl, value);
    }

    public RiskStatusDto Risk
    {
        get => _risk;
        private set => SetField(ref _risk, value);
    }

    public HermesStatusDto Hermes
    {
        get => _hermes;
        private set => SetField(ref _hermes, value);
    }

    public ObservableCollection<PositionDto> OpenPositions { get; } = [];
    public ObservableCollection<OrderDto> ActiveOrders { get; } = [];

    protected override void Refresh()
    {
        Account = ReadModel.GetAccountSummary();
        Pnl = ReadModel.GetPnlSummary();
        Risk = ReadModel.GetRiskStatus();
        Hermes = ReadModel.GetHermesStatus();

        ReplaceCollection(OpenPositions, ReadModel.GetOpenPositions());
        ReplaceCollection(ActiveOrders, ReadModel.GetActiveOrders());
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
