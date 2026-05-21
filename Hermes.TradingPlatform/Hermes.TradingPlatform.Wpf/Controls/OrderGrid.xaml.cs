using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class OrderGrid : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(OrderGrid));

    public OrderGrid() => InitializeComponent();

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
}
