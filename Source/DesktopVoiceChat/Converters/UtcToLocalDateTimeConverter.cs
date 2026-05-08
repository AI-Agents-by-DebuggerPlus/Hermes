using System.Globalization;
using System.Windows.Data;

namespace DesktopVoiceChat.Converters;

/// <summary>Мгновение из БД (<c>timestamptz</c>) в строку в системной локали Windows.</summary>
public sealed class UtcToLocalDateTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTime local;
        if (value is DateTimeOffset dto)
        {
            if (dto == default)
            {
                return string.Empty;
            }

            local = dto.LocalDateTime;
        }
        else if (value is DateTime dt && dt != default)
        {
            var utc = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            };

            local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);
        }
        else
        {
            return string.Empty;
        }

        var format = parameter as string;
        return string.IsNullOrEmpty(format)
            ? local.ToString("g", culture)
            : local.ToString(format, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
