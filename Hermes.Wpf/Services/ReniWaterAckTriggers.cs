namespace Hermes.Wpf.Services;

/// <summary>Local chat phrases for Reni vodokanal ack / submit (no Hermes CLI).</summary>
public static class ReniWaterAckTriggers
{
    public static bool MatchesAck(string message, bool pendingAckExists)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        if (!ContainsAckPhrase(t))
        {
            return false;
        }

        if (pendingAckExists)
        {
            return true;
        }

        return t.Contains("вод", StringComparison.Ordinal)
               || t.Contains("показан", StringComparison.Ordinal)
               || t.Contains("водоканал", StringComparison.Ordinal)
               || t.Contains("reni", StringComparison.Ordinal);
    }

    private static bool ContainsAckPhrase(string t)
    {
        var phrases = new[]
        {
            "принял",
            "приняла",
            "принято",
            "понял",
            "поняла",
            "понятно",
            "подтверждаю",
        };

        foreach (var p in phrases)
        {
            if (t == p || t.StartsWith(p + " ", StringComparison.Ordinal) || t.EndsWith(" " + p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return t is "ok" or "ок";
    }
}
