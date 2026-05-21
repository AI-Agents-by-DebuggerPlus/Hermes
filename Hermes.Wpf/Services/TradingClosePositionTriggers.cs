using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

internal static class TradingClosePositionTriggers
{
    private static readonly Regex ClosePositionPattern = new(
        @"(закрой|закрыть|закрывай|close)\s+(позици[юиюе]|position)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool Matches(string? message) =>
        !string.IsNullOrWhiteSpace(message) && ClosePositionPattern.IsMatch(message.Trim());
}
