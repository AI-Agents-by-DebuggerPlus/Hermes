using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Hermes.InAppAssistant.Wpf;

public partial class MiniAssistantPanel : UserControl
{
    public MiniAssistantPanel() => InitializeComponent();

    public static readonly DependencyProperty EmbeddedProperty = DependencyProperty.Register(
        nameof(Embedded),
        typeof(bool),
        typeof(MiniAssistantPanel),
        new PropertyMetadata(false));

    public bool Embedded
    {
        get => (bool)GetValue(EmbeddedProperty);
        set => SetValue(EmbeddedProperty, value);
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class BoolToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Collapsed;
}

public sealed class UserBubbleBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush User = new(Color.FromRgb(0x2B, 0x52, 0x82));
    private static readonly SolidColorBrush Assistant = new(Color.FromRgb(0x22, 0x2C, 0x3A));

    static UserBubbleBrushConverter()
    {
        User.Freeze();
        Assistant.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? User : Assistant;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
