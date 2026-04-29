using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Converters;

public sealed class ConnectionStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
        {
            return Brushes.Gray;
        }

        return state switch
        {
            ConnectionState.Connected => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
            ConnectionState.Checking => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
            ConnectionState.Error => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
            _ => new SolidColorBrush(Color.FromRgb(149, 165, 166))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
