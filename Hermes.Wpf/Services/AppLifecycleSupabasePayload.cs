using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>Startup / lifecycle rows for Supabase messages (filtered on poll-back).</summary>
public static class AppLifecycleSupabasePayload
{
    public const string EventStartup = "startup";

    public static string BuildStartupJson(string appId, string? version = null)
    {
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hermes_app"] = appId.Trim(),
            ["event"] = EventStartup,
            ["version"] = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim(),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string BuildStartupVoiceLine(string appDisplayName) =>
        $"{appDisplayName.Trim()} started";

    public static string BuildSupabaseContent(string appDisplayName) =>
        BilingualSegmentFormatter.ToSupabaseContent(BuildStartupVoiceLine(appDisplayName));

    public static bool IsStartupPayload(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (IsStartupJson(content))
        {
            return true;
        }

        var plain = BilingualSegmentFormatter.TryExtractVoicePlainText(content);
        return plain is not null && plain.EndsWith(" started", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStartupJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content.Trim());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("event", out var ev)
                || !string.Equals(ev.GetString(), EventStartup, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return root.TryGetProperty("hermes_app", out var app)
                   && !string.IsNullOrWhiteSpace(app.GetString());
        }
        catch
        {
            return false;
        }
    }
}
