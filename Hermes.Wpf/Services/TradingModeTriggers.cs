using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Client-side phrases for trading persona on/off and switch confirmation.</summary>
public static class TradingModeTriggers
{
    private static readonly Regex EnableWordRegex = new(
        @"^\s*(трейдинг|trading)\s*([!.?…]*\s*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool MatchesEnable(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (MatchesAgentMode(message) || MatchesDisable(message))
        {
            return false;
        }

        var t = message.Trim();
        if (EnableWordRegex.IsMatch(t))
        {
            return true;
        }

        var lower = t.ToLowerInvariant();
        return lower.StartsWith("трейдинг ", StringComparison.Ordinal)
               || lower.StartsWith("trading ", StringComparison.Ordinal);
    }

    /// <summary>Only «трейдинг» / «trading» without a trading task in the same message.</summary>
    public static bool IsBareEnableCommand(string message) => EnableWordRegex.IsMatch(message.Trim());

    /// <summary>Only «режим агента» (no extra user task in the same line).</summary>
    public static bool IsBareAgentModeCommand(string message)
    {
        if (!MatchesAgentMode(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        return t is "режим агента"
               or "режим агент"
               or "agent mode"
               or "общий режим агента";
    }

    /// <summary>Return to general Hermes assistant (not trader-executor).</summary>
    public static bool MatchesAgentMode(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        return t.Contains("режим агента", StringComparison.Ordinal)
               || t.Contains("режим агент", StringComparison.Ordinal)
               || t.Equals("agent mode", StringComparison.Ordinal)
               || t.Contains("общий режим агента", StringComparison.Ordinal);
    }

    public static bool MatchesDisable(string message) => MatchesAgentMode(message);

    public static bool MatchesConfirmYes(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        if (MatchesConfirmNo(message))
        {
            return false;
        }

        if (MatchesEnable(message) || MatchesAgentMode(message))
        {
            return false;
        }

        var yes = new[]
        {
            "да",
            "yes",
            "ок",
            "ok",
            "okay",
            "конечно",
            "переключ",
            "включ",
            "давай",
            "подтверж",
            "согласен",
            "ага",
            "угу",
            "go",
        };

        foreach (var y in yes)
        {
            if (t == y || t.StartsWith(y + " ", StringComparison.Ordinal) || t.StartsWith(y + ",", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesConfirmNo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        var no = new[]
        {
            "нет",
            "no",
            "не над",
            "не нуж",
            "отмен",
            "остав",
            "не переключ",
            "общий режим",
            "без трейдинг",
            "без trading",
        };

        foreach (var n in no)
        {
            if (t == n || t.Contains(n, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
