using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class OrdersViewModel : TradingPageViewModel
{
    private readonly IVirtualExchange _exchange;

    public OrdersViewModel(TradingReadModel readModel, IVirtualExchange exchange)
        : base(readModel)
    {
        _exchange = exchange;

        OrderTypes = ["Market", "Limit", "Stop"];
        Sides = ["Buy", "Sell"];

        PlaceOrderCommand = new RelayCommand(
            _ => PlaceNewOrder(),
            _ => CanPlaceOrder());

        CancelOrderCommand = new RelayCommand(
            p =>
            {
                if (p is OrderDto order)
                {
                    var ok = _exchange.TryCancelOrder(order.Id);
                    MessageBox.Show(
                        ok ? $"Order {order.Id} cancelled." : $"Failed to cancel {order.Id}.",
                        "Orders",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            },
            p => p is OrderDto { Status: "Open" });

        ModifyOrderCommand = new RelayCommand(
            p =>
            {
                if (p is OrderDto order)
                {
                    MessageBox.Show(
                        $"Order {order.Id} modification is not implemented. Cancel and place a new order.",
                        "Orders",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            },
            p => p is OrderDto { Status: "Open" });

        NewOrderType = "Market";
        NewSide = "Buy";
        NewSymbol = "BTCUSDT";

        Refresh();
    }

    public ObservableCollection<OrderDto> Orders { get; } = [];
    public ObservableCollection<string> Symbols { get; } = [];
    public IReadOnlyList<string> OrderTypes { get; }
    public IReadOnlyList<string> Sides { get; }

    private string _newSymbol = "BTCUSDT";

    public string NewSymbol
    {
        get => _newSymbol;
        set => SetField(ref _newSymbol, value);
    }

    private string _newOrderType = "Limit";

    public string NewOrderType
    {
        get => _newOrderType;
        set
        {
            if (SetField(ref _newOrderType, value))
            {
                Raise(nameof(IsPriceRequired));
                PlaceOrderCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _newSide = "Buy";

    public string NewSide
    {
        get => _newSide;
        set => SetField(ref _newSide, value);
    }

    private string _newPriceText = "";

    public string NewPriceText
    {
        get => _newPriceText;
        set
        {
            if (SetField(ref _newPriceText, value))
            {
                PlaceOrderCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _newQuantityText = "0.01";

    public string NewQuantityText
    {
        get => _newQuantityText;
        set
        {
            if (SetField(ref _newQuantityText, value))
            {
                PlaceOrderCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _newReduceOnly;

    public bool NewReduceOnly
    {
        get => _newReduceOnly;
        set => SetField(ref _newReduceOnly, value);
    }

    public bool IsPriceRequired => NewOrderType is not "Market";

    public RelayCommand PlaceOrderCommand { get; }
    public RelayCommand CancelOrderCommand { get; }
    public RelayCommand ModifyOrderCommand { get; }

    protected override void Refresh()
    {
        Orders.Clear();
        foreach (var o in ReadModel.GetAllOrders())
        {
            Orders.Add(o);
        }

        var currentSymbols = ReadModel.GetMarketWatch().Select(t => t.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in currentSymbols.Where(s => !Symbols.Contains(s)))
        {
            Symbols.Add(symbol);
        }

        if (Symbols.Count > 0 && !Symbols.Contains(NewSymbol))
        {
            NewSymbol = Symbols[0];
        }
    }

    private bool CanPlaceOrder()
    {
        if (!decimal.TryParse(NewQuantityText, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        {
            return false;
        }

        if (NewOrderType == "Market")
        {
            return true;
        }

        return decimal.TryParse(NewPriceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price > 0;
    }

    private void PlaceNewOrder()
    {
        if (!decimal.TryParse(NewQuantityText, NumberStyles.Any, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            ManualTradeNotifier.ReportWarning("Enter a valid quantity.");
            return;
        }

        var price = 0m;
        if (NewOrderType != "Market")
        {
            if (!decimal.TryParse(NewPriceText, NumberStyles.Any, CultureInfo.InvariantCulture, out price) || price <= 0)
            {
                ManualTradeNotifier.ReportWarning("Enter a valid price.");
                return;
            }
        }
        else
        {
            var ticker = ReadModel.GetMarketWatch().FirstOrDefault(t => t.Symbol == NewSymbol);
            price = ticker?.Price ?? 0m;
        }

        if (!Enum.TryParse<OrderType>(NewOrderType, ignoreCase: true, out var orderType))
        {
            ManualTradeNotifier.ReportWarning("Unknown order type.");
            return;
        }

        if (!Enum.TryParse<OrderSide>(NewSide, ignoreCase: true, out var side))
        {
            ManualTradeNotifier.ReportWarning("Unknown side.");
            return;
        }

        var order = _exchange.PlaceOrder(NewSymbol, orderType, side, quantity, price, NewReduceOnly);
        var status = order.Status.ToString();
        ManualTradeNotifier.ReportOrder(order, "New order");
    }
}
