using System.Text.Json;

namespace Hermes.Wpf.Services;

internal static class FlashcardRelayIntentParser
{
    internal sealed record FlashcardRelayIntentStart(string Topic, int IntervalMinutes, int DelayMinutes);

    internal enum FlashcardRelayIntentKind
    {
        None,
        Start,
        Stop
    }

    internal static bool TryConsumeIntent(string assistantText, out FlashcardRelayIntentKind kind,
        out FlashcardRelayIntentStart? startPayload)
    {
        kind = FlashcardRelayIntentKind.None;
        startPayload = null;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParseIntentJson(json, out kind, out startPayload))
            {
                continue;
            }

            if (kind != FlashcardRelayIntentKind.None)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>User-visible replacement when the assistant returns only structured skill JSON.</summary>
    internal static string UserFacingLine(FlashcardRelayIntentKind kind, FlashcardRelayIntentStart? start)
    {
        return kind switch
        {
            FlashcardRelayIntentKind.Stop => "[flashcards] Остановка цикла карточек.",
            FlashcardRelayIntentKind.Start when start != null =>
                $"[flashcards] Запланировано: «{start.Topic}» • интервал {start.IntervalMinutes} мин • старт через {start.DelayMinutes} мин.",
            _ => string.Empty
        };
    }

    private static bool TryParseIntentJson(string json, out FlashcardRelayIntentKind kind,
        out FlashcardRelayIntentStart? startPayload)
    {
        kind = FlashcardRelayIntentKind.None;
        startPayload = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk))
            {
                return false;
            }

            var name = sk.GetString()?.Trim().ToLowerInvariant();
            switch (name)
            {
                case "flashcard_stop":
                    kind = FlashcardRelayIntentKind.Stop;
                    return true;

                case "flashcard_start":
                    var topic = root.TryGetProperty("topic", out var tEl) ? (tEl.GetString() ?? string.Empty).Trim() : string.Empty;
                    if (topic.Length == 0)
                    {
                        topic = "(topic)";
                    }

                    var interval = ReadPositiveInt(root, "interval_minutes", @default: 15);
                    var delay = ReadNonNegativeInt(root, "delay_minutes", @default: 0);
                    kind = FlashcardRelayIntentKind.Start;
                    startPayload = new FlashcardRelayIntentStart(topic, interval, delay);
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static int ReadPositiveInt(JsonElement root, string name, int @default)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return @default;
        }

        try
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => Math.Max(1, el.TryGetInt32(out var i) ? i : @default),
                JsonValueKind.String => Math.Max(1, int.TryParse(el.GetString(), out var s) ? s : @default),
                _ => @default
            };
        }
        catch
        {
            return @default;
        }
    }

    private static int ReadNonNegativeInt(JsonElement root, string name, int @default)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return @default;
        }

        try
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => Math.Max(0, el.TryGetInt32(out var i) ? i : @default),
                JsonValueKind.String => Math.Max(0, int.TryParse(el.GetString(), out var s) ? s : @default),
                _ => @default
            };
        }
        catch
        {
            return @default;
        }
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0 && trimmed.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase))
        {
            yield return trimmed;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            var depth = 0;
            for (var j = i; j < text.Length; j++)
            {
                if (text[j] == '{')
                {
                    depth++;
                }
                else if (text[j] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var slice = text[i..(j + 1)];
                        if (slice.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return slice;
                        }

                        break;
                    }
                }
            }
        }
    }
}
