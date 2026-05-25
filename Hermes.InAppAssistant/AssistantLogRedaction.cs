namespace Hermes.InAppAssistant;

public static class AssistantLogRedaction
{
    public static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "(empty)";
        }

        var t = key.Trim();
        return t.Length <= 8 ? "••••••••" : $"{t[..4]}…{t[^4..]}";
    }

    public static string Preview(string? text, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var t = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= max ? t : t[..max] + "…";
    }
}
