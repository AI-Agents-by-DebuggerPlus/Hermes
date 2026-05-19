using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Hermes.WpGallery;
using Hermes.WpGallery.Tool.Models;

namespace Hermes.WpGallery.Tool.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is not Visibility.Visible;
}

[ValueConversion(typeof(string), typeof(string))]
public class RestEndpointConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is string url && WpGalleryEndpoints.TryNormalizeSiteUrl(url, out var site, out _))
            return WpGalleryEndpoints.BuildImageUrl(site);
        return "";
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class HexColorBrushConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(v?.ToString() ?? "#64748b");
            return new SolidColorBrush(color);
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>ComboBox index 0 = all monitors (-1), index 1 = monitor 0, …</summary>
[ValueConversion(typeof(int), typeof(int))]
public class MonitorIndexComboConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int idx ? idx + 1 : 0;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is int comboIdx ? comboIdx - 1 : -1;
}

[ValueConversion(typeof(LogLevel), typeof(SolidColorBrush))]
public class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is LogLevel level)
            return level switch
            {
                LogLevel.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                LogLevel.Error   => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                _                => new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            };
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
