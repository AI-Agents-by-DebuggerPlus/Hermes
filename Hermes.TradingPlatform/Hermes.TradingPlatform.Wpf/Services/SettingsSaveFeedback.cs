namespace Hermes.TradingPlatform.Wpf.Services;

public static class SettingsSaveFeedback
{
    public static string OpenRouterSaved(string? apiKey, string model, string filePath)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return $"Saved to {filePath}: OpenRouter API key is empty — the assistant (✦) will not respond until a key is set.";
        }

        return $"Saved to {filePath}: OpenRouter key {MaskSecret(apiKey)}, model {model}.";
    }

    public static string MarketDataApplied(string mode, string feedStatus) =>
        $"Market data source: {mode}. Status: {feedStatus}.";

    public static string HermesOrchestration(bool enabled) =>
        enabled
            ? "Hermes orchestration enabled and saved to platform-settings.json."
            : "Hermes orchestration disabled and saved to platform-settings.json.";

    public static string TradingSounds(bool enabled) =>
        enabled
            ? "Trade sounds enabled and saved."
            : "Trade sounds disabled and saved.";

    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var t = value.Trim();
        return t.Length <= 8 ? "••••••••" : $"{t[..4]}…{t[^4..]}";
    }
}
