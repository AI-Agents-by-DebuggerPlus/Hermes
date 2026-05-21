using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages.Items;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class MarketWatchViewModel : BaseViewModel
{
    public MarketWatchViewModel(MockTradingDataService data)
    {
        foreach (var t in data.GetMarketWatch())
        {
            Tickers.Add(new MarketTickerItemViewModel(t.Symbol, t.Price, t.ChangePercent24h, t.Volume24h, t.InWatchlist));
        }

        ToggleWatchlistCommand = new RelayCommand(p =>
        {
            if (p is MarketTickerItemViewModel item)
            {
                item.InWatchlist = !item.InWatchlist;
            }
        });
    }

    public ObservableCollection<MarketTickerItemViewModel> Tickers { get; } = [];
    public RelayCommand ToggleWatchlistCommand { get; }
}
