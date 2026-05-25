using System.Windows;
using System.Windows.Controls;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class HelpHintIcon : UserControl
{
    public HelpHintIcon()
    {
        InitializeComponent();
        PreviewMouseLeftButtonDown += static (_, e) => e.Handled = true;
    }

    public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
        nameof(HelpText),
        typeof(string),
        typeof(HelpHintIcon),
        new PropertyMetadata(string.Empty));

    public string HelpText
    {
        get => (string)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }
}
