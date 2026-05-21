namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages.Items;

public sealed class MarketTickerItemViewModel : ViewModels.BaseViewModel
{
    public MarketTickerItemViewModel(string symbol, decimal price, decimal changePercent24h, decimal volume24h, bool inWatchlist)
    {
        Symbol = symbol;
        _price = price;
        _changePercent24h = changePercent24h;
        _volume24h = volume24h;
        _inWatchlist = inWatchlist;
    }

    public string Symbol { get; }

    private decimal _price;

    public decimal Price
    {
        get => _price;
        private set => SetField(ref _price, value);
    }

    private decimal _changePercent24h;

    public decimal ChangePercent24h
    {
        get => _changePercent24h;
        private set
        {
            if (SetField(ref _changePercent24h, value))
            {
                Raise(nameof(ChangeDisplay));
            }
        }
    }

    private decimal _volume24h;

    public decimal Volume24h
    {
        get => _volume24h;
        private set => SetField(ref _volume24h, value);
    }

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

    public void UpdateFrom(decimal price, decimal changePercent24h, decimal volume24h, bool inWatchlist)
    {
        Price = price;
        ChangePercent24h = changePercent24h;
        Volume24h = volume24h;
        InWatchlist = inWatchlist;
    }
}
