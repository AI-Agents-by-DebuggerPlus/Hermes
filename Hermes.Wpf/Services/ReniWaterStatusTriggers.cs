namespace Hermes.Wpf.Services;

/// <summary>User questions about Reni Water history/status (local reply, no CLI).</summary>
public static class ReniWaterStatusTriggers
{
    public static bool Matches(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        if (!HasWaterContext(t))
        {
            return false;
        }

        if (ReniWaterSubmitTriggers.MatchesSubmit(message)
            || ReniWaterScheduleParser.IsSchedulePhrase(message))
        {
            return false;
        }

        return t.Contains("передавал", StringComparison.Ordinal)
               || t.Contains("передавали", StringComparison.Ordinal)
               || t.Contains("когда последн", StringComparison.Ordinal)
               || t.Contains("настроен", StringComparison.Ordinal)
               || t.Contains("статус", StringComparison.Ordinal)
               || t.Contains("помнишь", StringComparison.Ordinal)
               || t.Contains("ты переда", StringComparison.Ordinal)
               || t.Contains("did you submit", StringComparison.Ordinal)
               || t.Contains("reni water", StringComparison.Ordinal) && t.Contains('?');
    }

    private static bool HasWaterContext(string t) =>
        t.Contains("показан", StringComparison.Ordinal)
        || t.Contains("водоканал", StringComparison.Ordinal)
        || t.Contains("вод", StringComparison.Ordinal)
        || t.Contains("рени", StringComparison.Ordinal)
        || t.Contains("reni", StringComparison.Ordinal);
}
