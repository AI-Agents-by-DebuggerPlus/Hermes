using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class HermesViewModel : TradingPageViewModel
{
    public HermesViewModel(TradingReadModel readModel)
        : base(readModel) => Refresh();

    private HermesStatusDto _status = new()
    {
        State = "Monitoring",
        ActiveStrategy = "",
        Mode = "Orchestration / Paper",
    };

    private string _currentReasoning = "";
    private string _strategyContext = "";

    public HermesStatusDto Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string CurrentReasoning
    {
        get => _currentReasoning;
        private set => SetField(ref _currentReasoning, value);
    }

    public string StrategyContext
    {
        get => _strategyContext;
        private set => SetField(ref _strategyContext, value);
    }

    public ObservableCollection<HermesTaskDto> Tasks { get; } = [];
    public ObservableCollection<HermesDecisionDto> Decisions { get; } = [];

    protected override void Refresh()
    {
        Status = ReadModel.GetHermesStatus();
        CurrentReasoning = ReadModel.GetHermesReasoning();
        StrategyContext = ReadModel.GetHermesStrategyContext();

        Tasks.Clear();
        foreach (var t in ReadModel.GetHermesTasks())
        {
            Tasks.Add(t);
        }

        Decisions.Clear();
        foreach (var d in ReadModel.GetHermesDecisions())
        {
            Decisions.Add(d);
        }
    }
}
