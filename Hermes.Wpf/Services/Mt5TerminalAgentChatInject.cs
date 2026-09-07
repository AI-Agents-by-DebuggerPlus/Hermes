using System.IO;
using System.Text;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>
/// HWT (and other tools) can drop a human-language prompt here; Hermes.Wpf injects it
/// into the Mt5Terminal chat pipeline (same path as typing in the chat box).
/// </summary>
public static class Mt5TerminalAgentChatInject
{
    public const string FileName = "agent_chat_inject.json";

    public static string ResolvePath(string? projectWindowsPath = null) =>
        Path.Combine(Mt5TerminalIpcClient.ResolveIpcDir(projectWindowsPath), FileName);

    public static void Write(string text, string? projectWindowsPath = null, string? source = null)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Empty inject text.", nameof(text));
        }

        var path = ResolvePath(projectWindowsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"),
            text = trimmed,
            source = string.IsNullOrWhiteSpace(source) ? "hwt" : source.Trim(),
            utc = DateTime.UtcNow.ToString("o"),
        });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, payload, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tmp, path);
    }

    public static bool TryConsume(string? projectWindowsPath, out string text, out string id)
    {
        text = string.Empty;
        id = string.Empty;
        var path = ResolvePath(projectWindowsPath);
        if (!File.Exists(path))
        {
            return false;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return false;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // another consumer may own it
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? string.Empty).Trim()
                : string.Empty;
            id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String
                ? (i.GetString() ?? string.Empty).Trim()
                : string.Empty;
        }
        catch (JsonException)
        {
            text = raw.Trim();
        }

        return text.Length > 0;
    }
}
