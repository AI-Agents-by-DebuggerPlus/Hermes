using System.Windows;
using System.Windows.Controls;

namespace Hermes.BinanceDemoFuturesTerminal.Controls;

public partial class InfoHintButton : UserControl
{
    public static readonly DependencyProperty HintTextProperty =
        DependencyProperty.Register(nameof(HintText), typeof(string), typeof(InfoHintButton), new PropertyMetadata(string.Empty));

    public string HintText
    {
        get => (string)GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    public InfoHintButton() => InitializeComponent();
}
