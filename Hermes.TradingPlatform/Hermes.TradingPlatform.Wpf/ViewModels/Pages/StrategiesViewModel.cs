using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class StrategiesViewModel : TradingPageViewModel
{
    private readonly TradingPlatformHost _host;

    public StrategiesViewModel(TradingReadModel readModel, TradingPlatformHost host)
        : base(readModel)
    {
        _host = host;

        ToggleStrategyCommand = new RelayCommand(p =>
        {
            if (p is StrategyCardItemViewModel card)
            {
                var next = !card.IsEnabled;
                card.IsEnabled = next;
                card.Status = next ? "Running" : "Idle";
                _host.SetStrategyEnabled(card.Id, next);
            }
        });

        Refresh();
    }

    public ObservableCollection<StrategyCardItemViewModel> Strategies { get; } = [];
    public RelayCommand ToggleStrategyCommand { get; }

    protected override void Refresh()
    {
        Strategies.Clear();
        foreach (var s in ReadModel.GetStrategies())
        {
            Strategies.Add(new StrategyCardItemViewModel
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                RiskProfile = s.RiskProfile,
                Status = s.Status,
                IsEnabled = s.IsEnabled,
            });
        }
    }
}
