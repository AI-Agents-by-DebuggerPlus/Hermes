using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages.Items;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class MarketWatchViewModel : TradingPageViewModel
{
    private readonly IVirtualExchange _exchange;
    private readonly Dictionary<string, MarketTickerItemViewModel> _bySymbol = new(StringComparer.OrdinalIgnoreCase);

    public MarketWatchViewModel(TradingReadModel readModel, IVirtualExchange exchange)
        : base(readModel)
    {
        _exchange = exchange;

        ToggleWatchlistCommand = new RelayCommand(p =>
        {
            if (p is MarketTickerItemViewModel item)
            {
                item.InWatchlist = !item.InWatchlist;
            }
        });

        OpenLongCommand = new RelayCommand(
            p => OpenFromTicker(p as MarketTickerItemViewModel, OrderSide.Buy),
            p => p is MarketTickerItemViewModel);
        OpenShortCommand = new RelayCommand(
            p => OpenFromTicker(p as MarketTickerItemViewModel, OrderSide.Sell),
            p => p is MarketTickerItemViewModel);
        ClosePositionCommand = new RelayCommand(
            p => CloseFromTicker(p as MarketTickerItemViewModel),
            p => p is MarketTickerItemViewModel);

        Refresh();
    }

    public ObservableCollection<MarketTickerItemViewModel> Tickers { get; } = [];

    private string _tradeQuantityText = "0.01";

    public string TradeQuantityText
    {
        get => _tradeQuantityText;
        set
        {
            if (SetField(ref _tradeQuantityText, value))
            {
                OpenLongCommand.RaiseCanExecuteChanged();
                OpenShortCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ToggleWatchlistCommand { get; }
    public RelayCommand OpenLongCommand { get; }
    public RelayCommand OpenShortCommand { get; }
    public RelayCommand ClosePositionCommand { get; }

    protected override void Refresh()
    {
        foreach (var dto in ReadModel.GetMarketWatch())
        {
            if (_bySymbol.TryGetValue(dto.Symbol, out var existing))
            {
                existing.UpdateFrom(dto.Price, dto.ChangePercent24h, dto.Volume24h, dto.InWatchlist);
                continue;
            }

            var item = new MarketTickerItemViewModel(
                dto.Symbol,
                dto.Price,
                dto.ChangePercent24h,
                dto.Volume24h,
                dto.InWatchlist);
            _bySymbol[dto.Symbol] = item;
            Tickers.Add(item);
        }
    }

    private void OpenFromTicker(MarketTickerItemViewModel? item, OrderSide side)
    {
        if (item is null)
        {
            return;
        }

        if (!ManualTradeNotifier.TryParseQuantity(TradeQuantityText, out var quantity))
        {
            ManualTradeNotifier.ReportWarning("Укажите Qty в панели выше.");
            return;
        }

        var price = item.Price > 0 ? item.Price : ManualTradeNotifier.ResolveMarketPrice(ReadModel, item.Symbol);
        var order = _exchange.PlaceOrder(item.Symbol, OrderType.Market, side, quantity, price, reduceOnly: false);
        ManualTradeNotifier.ReportOrder(order, $"{item.Symbol} {(side == OrderSide.Buy ? "Long" : "Short")}");
    }

    private void CloseFromTicker(MarketTickerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var order = _exchange.ClosePosition(item.Symbol);
        ManualTradeNotifier.ReportOrder(order, $"Закрыть {item.Symbol}");
    }
}
