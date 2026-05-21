using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class HermesViewModel : BaseViewModel
{
    public HermesViewModel(MockTradingDataService data)
    {
        Status = data.GetHermesStatus();
        CurrentReasoning =
            "Monitoring liquidity sweep setup on BTCUSDT. No new entries until daily drawdown headroom > 20%. " +
            "ETH short within risk profile.";
        StrategyContext = "Active: Liquidity Sweep (paper). Watchlist: BTC, ETH, SOL.";

        foreach (var t in data.GetHermesTasks())
        {
            Tasks.Add(t);
        }

        foreach (var d in data.GetHermesDecisions())
        {
            Decisions.Add(d);
        }
    }

    public HermesStatusDto Status { get; }
    public string CurrentReasoning { get; }
    public string StrategyContext { get; }
    public ObservableCollection<HermesTaskDto> Tasks { get; } = [];
    public ObservableCollection<HermesDecisionDto> Decisions { get; } = [];
}
