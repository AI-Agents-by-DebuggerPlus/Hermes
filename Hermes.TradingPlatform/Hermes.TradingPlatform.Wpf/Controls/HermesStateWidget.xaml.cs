using System.Windows;
using System.Windows.Controls;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class HermesStateWidget : UserControl
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(string), typeof(HermesStateWidget));

    public static readonly DependencyProperty ActiveStrategyProperty =
        DependencyProperty.Register(nameof(ActiveStrategy), typeof(string), typeof(HermesStateWidget));

    public static readonly DependencyProperty ConfidenceProperty =
        DependencyProperty.Register(nameof(Confidence), typeof(decimal), typeof(HermesStateWidget));

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(string), typeof(HermesStateWidget));

    public HermesStateWidget() => InitializeComponent();

    public string State
    {
        get => (string)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string ActiveStrategy
    {
        get => (string)GetValue(ActiveStrategyProperty);
        set => SetValue(ActiveStrategyProperty, value);
    }

    public decimal Confidence
    {
        get => (decimal)GetValue(ConfidenceProperty);
        set => SetValue(ConfidenceProperty, value);
    }

    public string Mode
    {
        get => (string)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }
}
