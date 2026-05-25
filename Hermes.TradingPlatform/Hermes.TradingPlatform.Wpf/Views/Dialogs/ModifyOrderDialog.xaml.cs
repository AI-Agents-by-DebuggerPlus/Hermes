using System.Globalization;
using System.Windows;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Mock;

namespace Hermes.TradingPlatform.Wpf.Views.Dialogs;

public partial class ModifyOrderDialog : Window
{
    public ModifyOrderDialog(OrderDto order)
    {
        InitializeComponent();
        SourceOrder = order;
        HeaderText.Text =
            $"Order {order.Id} · {order.Symbol} · {order.Side} {order.Type}\n" +
            $"Modifying replaces the order: cancel + place new with risk re-validation.";
        PriceBox.Text = order.Price.ToString("0.########", CultureInfo.InvariantCulture);
        QuantityBox.Text = order.Quantity.ToString("0.########", CultureInfo.InvariantCulture);

        if (Enum.TryParse<OrderType>(order.Type, ignoreCase: true, out var t) && t == OrderType.Stop)
        {
            TriggerBox.Text = order.Price.ToString("0.########", CultureInfo.InvariantCulture);
            TriggerBox.IsEnabled = true;
        }
        else
        {
            TriggerBox.Text = string.Empty;
            TriggerBox.IsEnabled = false;
        }
    }

    public OrderDto SourceOrder { get; }
    public decimal NewPrice { get; private set; }
    public decimal NewQuantity { get; private set; }
    public decimal? NewTrigger { get; private set; }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(PriceBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var price)
            || price <= 0)
        {
            StatusText.Text = "Enter a valid positive price.";
            return;
        }

        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty)
            || qty <= 0)
        {
            StatusText.Text = "Enter a valid positive quantity.";
            return;
        }

        decimal? trigger = null;
        if (TriggerBox.IsEnabled)
        {
            if (!decimal.TryParse(TriggerBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t) || t <= 0)
            {
                StatusText.Text = "Enter a valid trigger price for Stop order.";
                return;
            }

            trigger = t;
        }

        NewPrice = price;
        NewQuantity = qty;
        NewTrigger = trigger;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
