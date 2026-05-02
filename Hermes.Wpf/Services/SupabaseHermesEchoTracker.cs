using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Suppress duplicate Hermes bubbles when our own INSERT is polled back from Supabase.</summary>
internal sealed class SupabaseHermesEchoTracker
{
    private readonly object _gate = new();
    private readonly List<PendingOutbound> _pending = [];

    private sealed record PendingOutbound(string CanonicalSenderName, string NormalizedContent, DateTime UtcEnqueued);

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
        }
    }

    public void RegisterAfterSuccessfulPublish(string canonicalHermesSenderName, string outboundContent)
    {
        lock (_gate)
        {
            PruneLocked();
            var sender = NormalizeSender(canonicalHermesSenderName);
            var body = NormalizeContent(outboundContent);
            if (sender.Length > 0 && body.Length > 0)
            {
                _pending.Add(new PendingOutbound(sender, body, DateTime.UtcNow));
            }
        }
    }

    /// <returns><c>true</c> if this row should be suppressed (recent echo of our own assistant message).</returns>
    public bool TryConsumeEcho(SupabaseMessageRow row, string canonicalHermesSenderName)
    {
        var canon = NormalizeSender(canonicalHermesSenderName);
        if (!string.Equals(NormalizeSender(row.SenderName), canon, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = NormalizeContent(row.Content);

        lock (_gate)
        {
            PruneLocked();
            var matchIndex = _pending.FindIndex(p =>
                string.Equals(p.CanonicalSenderName, canon, StringComparison.OrdinalIgnoreCase)
                && p.NormalizedContent == content);
            if (matchIndex >= 0)
            {
                _pending.RemoveAt(matchIndex);
                return true;
            }
        }

        return false;
    }

    private void PruneLocked()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        _pending.RemoveAll(p => p.UtcEnqueued < cutoff);
    }

    private static string NormalizeSender(string? v) =>
        (v ?? string.Empty).Trim();

    private static string NormalizeContent(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
        {
            return string.Empty;
        }

        return v.ReplaceLineEndings("\n").Trim();
    }
}
