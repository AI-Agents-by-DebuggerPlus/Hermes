using System.Collections.ObjectModel;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class PositionsViewModel : TradingPageViewModel
{
    private readonly IVirtualExchange _exchange;

    public PositionsViewModel(TradingReadModel readModel, IVirtualExchange exchange)
        : base(readModel)
    {
        _exchange = exchange;
        OpenLongCommand = new RelayCommand(_ => OpenMarket(OrderSide.Buy), _ => CanOpenMarket());
        OpenShortCommand = new RelayCommand(_ => OpenMarket(OrderSide.Sell), _ => CanOpenMarket());
        ClosePositionCommand = new RelayCommand(
            p =>
            {
                if (p is PositionDto pos)
                {
                    CloseOne(pos.Symbol);
                }
            },
            p => p is PositionDto);
        CloseAllCommand = new RelayCommand(_ => CloseAll(), _ => Positions.Count > 0);

        Refresh();
    }

    public ObservableCollection<PositionDto> Positions { get; } = [];
    public ObservableCollection<TradeJournalEntryDto> Journal { get; } = [];
    public ObservableCollection<string> Symbols { get; } = [];

    private string _tradeSymbol = "BTCUSDT";

    public string TradeSymbol
    {
        get => _tradeSymbol;
        set
        {
            if (SetField(ref _tradeSymbol, value))
            {
                OpenLongCommand?.RaiseCanExecuteChanged();
                OpenShortCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _tradeQuantityText = "0.01";

    public string TradeQuantityText
    {
        get => _tradeQuantityText;
        set
        {
            if (SetField(ref _tradeQuantityText, value))
            {
                OpenLongCommand?.RaiseCanExecuteChanged();
                OpenShortCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand OpenLongCommand { get; }
    public RelayCommand OpenShortCommand { get; }
    public RelayCommand ClosePositionCommand { get; }
    public RelayCommand CloseAllCommand { get; }

    protected override void Refresh()
    {
        Positions.Clear();
        foreach (var p in ReadModel.GetOpenPositions())
        {
            Positions.Add(p);
        }

        Journal.Clear();
        foreach (var entry in ReadModel.GetJournal())
        {
            Journal.Add(entry);
        }

        CloseAllCommand.RaiseCanExecuteChanged();

        var watch = ReadModel.GetMarketWatch().Select(t => t.Symbol).ToList();
        foreach (var symbol in watch.Where(s => !Symbols.Contains(s)))
        {
            Symbols.Add(symbol);
        }

        if (Symbols.Count > 0 && !Symbols.Contains(TradeSymbol))
        {
            TradeSymbol = Symbols[0];
        }
    }

    private bool CanOpenMarket() =>
        ManualTradeNotifier.TryParseQuantity(TradeQuantityText, out _)
        && !string.IsNullOrWhiteSpace(TradeSymbol);

    private void OpenMarket(OrderSide side)
    {
        if (!ManualTradeNotifier.TryParseQuantity(TradeQuantityText, out var quantity))
        {
            ManualTradeNotifier.ReportWarning("Enter a valid quantity.");
            return;
        }

        var price = ManualTradeNotifier.ResolveMarketPrice(ReadModel, TradeSymbol);
        if (price <= 0)
        {
            ManualTradeNotifier.ReportWarning("No price for symbol — wait for a market data tick.");
            return;
        }

        var order = _exchange.PlaceOrder(TradeSymbol, OrderType.Market, side, quantity, price, reduceOnly: false);
        ManualTradeNotifier.ReportOrder(order, side == OrderSide.Buy ? "Long (market)" : "Short (market)");
    }

    private void CloseOne(string symbol)
    {
        var order = _exchange.ClosePosition(symbol);
        ManualTradeNotifier.ReportOrder(order, "Close position");
    }

    private void CloseAll()
    {
        var symbols = Positions.Select(p => p.Symbol).Distinct().ToList();
        foreach (var symbol in symbols)
        {
            var order = _exchange.ClosePosition(symbol);
            ManualTradeNotifier.ReportOrder(order, $"Close all · {symbol}");
        }

        if (symbols.Count > 0)
        {
            ManualTradeNotifier.ReportInfo($"Closing {symbols.Count} positions submitted to journal.");
        }
    }
}
