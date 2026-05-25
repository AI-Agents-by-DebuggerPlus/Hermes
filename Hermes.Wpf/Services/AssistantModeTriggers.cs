using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

public static class AssistantModeTriggers
{
    private static readonly Regex EnableRegex = new(
        @"^\s*(режим\s+ассистента|assistant\s+mode|ассистент)\s*([!.?…]*\s*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool MatchesEnable(string message) =>
        !string.IsNullOrWhiteSpace(message) && EnableRegex.IsMatch(message.Trim());

    public static bool IsBareEnableCommand(string message) => MatchesEnable(message);
}
