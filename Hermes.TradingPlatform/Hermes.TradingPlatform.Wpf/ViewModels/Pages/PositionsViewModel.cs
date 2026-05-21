using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class PositionsViewModel : BaseViewModel
{
    public PositionsViewModel(MockTradingDataService data)
    {
        foreach (var p in data.GetOpenPositions())
        {
            Positions.Add(p);
        }
    }

    public ObservableCollection<PositionDto> Positions { get; } = [];
}
