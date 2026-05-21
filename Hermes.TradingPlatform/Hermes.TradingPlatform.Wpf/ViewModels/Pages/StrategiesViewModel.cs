using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class StrategiesViewModel : BaseViewModel
{
    public StrategiesViewModel(MockTradingDataService data)
    {
        foreach (var s in data.GetStrategies())
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

        ToggleStrategyCommand = new RelayCommand(p =>
        {
            if (p is StrategyCardItemViewModel card)
            {
                card.IsEnabled = !card.IsEnabled;
            }
        });
    }

    public ObservableCollection<StrategyCardItemViewModel> Strategies { get; } = [];
    public RelayCommand ToggleStrategyCommand { get; }
}
