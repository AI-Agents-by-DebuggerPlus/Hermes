using System;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

public enum EnglishNavCommand
{
    None = 0,
    FullScreen = 1,
    Next = 2,
    Previous = 3,
    Exit = 4,
}

/// <summary>Parses AndroidChat → EnglishLearning remote nav payloads.</summary>
public static class EnglishNavParser
{
    public static bool TryParse(string content, out EnglishNavCommand command)
    {
        command = EnglishNavCommand.None;
        if (string.IsNullOrWhiteSpace(content)) return false;
        var t = content.Trim();

        if (t.StartsWith("[NAV:", StringComparison.OrdinalIgnoreCase) && t.EndsWith("]"))
        {
            var inner = t.Substring(5, t.Length - 6).Trim();
            command = Map(inner);
            return command != EnglishNavCommand.None;
        }

        if (t.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var obj = JObject.Parse(t);
                var type = obj["type"]?.ToString() ?? string.Empty;
                if (!string.Equals(type, "english_nav", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "english_learning_nav", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "nav", StringComparison.OrdinalIgnoreCase))
                {
                    var onlyCmd = obj["command"]?.ToString() ?? obj["action"]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(onlyCmd) && obj["markdown"] == null && obj["type"] == null)
                    {
                        command = Map(onlyCmd);
                        return command != EnglishNavCommand.None;
                    }

                    return false;
                }

                var cmd = obj["command"]?.ToString()
                          ?? obj["action"]?.ToString()
                          ?? obj["nav"]?.ToString()
                          ?? string.Empty;
                command = Map(cmd);
                return command != EnglishNavCommand.None;
            }
            catch
            {
                return false;
            }
        }

        command = Map(t);
        return command != EnglishNavCommand.None
               && (string.Equals(t, "fullscreen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "full_screen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "full screen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "next", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "previous", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "prev", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "exit", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "close", StringComparison.OrdinalIgnoreCase));
    }

    private static EnglishNavCommand Map(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EnglishNavCommand.None;
        var c = raw.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        return c switch
        {
            "fullscreen" or "fullscr" or "fs" or "togglefullscreen" => EnglishNavCommand.FullScreen,
            "next" or "nextscreen" or "forward" or "right" => EnglishNavCommand.Next,
            "previous" or "prev" or "prevscreen" or "back" or "left" => EnglishNavCommand.Previous,
            "exit" or "close" or "quit" => EnglishNavCommand.Exit,
            _ => EnglishNavCommand.None,
        };
    }
}
