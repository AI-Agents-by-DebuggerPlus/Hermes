namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages.Items;

public sealed class MarketTickerItemViewModel : ViewModels.BaseViewModel
{
    public MarketTickerItemViewModel(string symbol, decimal price, decimal changePercent24h, decimal volume24h, bool inWatchlist)
    {
        Symbol = symbol;
        Price = price;
        ChangePercent24h = changePercent24h;
        Volume24h = volume24h;
        _inWatchlist = inWatchlist;
    }

    public string Symbol { get; }
    public decimal Price { get; }
    public decimal ChangePercent24h { get; }
    public decimal Volume24h { get; }

    private bool _inWatchlist;

    public bool InWatchlist
    {
        get => _inWatchlist;
        set
        {
            if (SetField(ref _inWatchlist, value))
            {
                Raise(nameof(WatchlistStar));
            }
        }
    }

    public string ChangeDisplay => $"{ChangePercent24h:+#0.00;-#0.00;0.00}%";

    public string WatchlistStar => InWatchlist ? "★" : "○";
}
