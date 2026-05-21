using System.Collections.ObjectModel;
using System.Windows;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class OrdersViewModel : BaseViewModel
{
    public OrdersViewModel(MockTradingDataService data)
    {
        foreach (var o in data.GetAllOrders())
        {
            Orders.Add(o);
        }

        CancelOrderCommand = new RelayCommand(
            p =>
            {
                if (p is OrderDto order)
                {
                    MessageBox.Show($"[Mock] Cancel order {order.Id}", "Orders", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            },
            p => p is OrderDto { Status: "Open" });

        ModifyOrderCommand = new RelayCommand(
            p =>
            {
                if (p is OrderDto order)
                {
                    MessageBox.Show($"[Mock] Modify order {order.Id}", "Orders", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            },
            p => p is OrderDto { Status: "Open" });
    }

    public ObservableCollection<OrderDto> Orders { get; } = [];
    public RelayCommand CancelOrderCommand { get; }
    public RelayCommand ModifyOrderCommand { get; }
}
