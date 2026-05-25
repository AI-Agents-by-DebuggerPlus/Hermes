using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>Parses inbound server JSON like {"mode": "assistant"} for local mode switches.</summary>
internal static class ServerModeCommandParser
{
    internal static bool TryParse(string payload, out string? mode)
    {
        mode = null;
        foreach (var candidate in EnumerateJsonObjectCandidates(payload))
        {
            if (TryParseModeFromJson(candidate, out mode))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<string> EnumerateJsonObjectCandidates(string? payload)
    {
        var t = (payload ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            yield break;
        }

        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] != '{')
            {
                continue;
            }

            var end = FindMatchingObjectEnd(t, i);
            if (end < 0)
            {
                continue;
            }

            yield return t[i..(end + 1)];
            i = end;
        }
    }

    internal static bool IsAssistantMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }

        var m = mode.Trim().ToLowerInvariant();
        return m is "assistant" or "assisant";
    }

    private static bool TryParseModeFromJson(string json, out string? mode)
    {
        mode = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.Equals("mode", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (prop.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                mode = prop.Value.GetString()?.Trim();
                return !string.IsNullOrWhiteSpace(mode);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static int FindMatchingObjectEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
