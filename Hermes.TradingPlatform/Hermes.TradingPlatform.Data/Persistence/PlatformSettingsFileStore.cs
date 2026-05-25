using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Settings;

namespace Hermes.TradingPlatform.Data.Persistence;

public sealed class PlatformSettingsFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PlatformSettingsFileStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);
        FilePath = filePath ?? Path.Combine(dir, "platform-settings.json");
    }

    public string FilePath { get; }

    public PlatformSettingsDto Load()
    {
        if (!File.Exists(FilePath))
        {
            return TryImportOpenRouterFromHermesWpf(new PlatformSettingsDto());
        }

        try
        {
            var raw = File.ReadAllText(FilePath);
            var dto = JsonSerializer.Deserialize<PlatformSettingsDto>(raw, JsonOptions)
                      ?? new PlatformSettingsDto();
            var marketDataBefore = dto.MarketDataSource;
            dto = MigrateInAppAssistantLegacy(dto, raw);
            dto = MigrateMarketDataToBinanceFutures(dto);
            if (!string.Equals(marketDataBefore, dto.MarketDataSource, StringComparison.OrdinalIgnoreCase))
            {
                Save(dto);
            }

            return TryImportOpenRouterFromHermesWpf(dto);
        }
        catch
        {
            return new PlatformSettingsDto();
        }
    }

    public void Save(PlatformSettingsDto settings) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));

    public static MarketDataSource ParseSource(string? value) =>
        string.Equals(value, "BinanceFutures", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Binance Futures (live)", StringComparison.OrdinalIgnoreCase)
            ? MarketDataSource.BinanceFutures
            : MarketDataSource.Mock;

    public static string ToDisplayName(MarketDataSource source) => source switch
    {
        MarketDataSource.BinanceFutures => "Binance Futures (live)",
        _ => "Mock (simulation)",
    };

    public static string ToStorageValue(MarketDataSource source) => source switch
    {
        MarketDataSource.BinanceFutures => "BinanceFutures",
        _ => "Mock",
    };

    /// <summary>Upgrade legacy default Mock → live Binance USDT-M ticker stream.</summary>
    private static PlatformSettingsDto MigrateMarketDataToBinanceFutures(PlatformSettingsDto dto)
    {
        if (string.Equals(dto.MarketDataSource, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            dto.MarketDataSource = "BinanceFutures";
        }

        return dto;
    }

    private static PlatformSettingsDto MigrateInAppAssistantLegacy(PlatformSettingsDto dto, string rawJson)
    {
        if (!string.IsNullOrWhiteSpace(dto.InAppAssistantOpenRouterApiKey))
        {
            return dto;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var key = ReadJsonString(root, "InAppAssistantOpenRouterApiKey");
            if (string.IsNullOrWhiteSpace(key))
            {
                key = ReadJsonString(root, "InAppAssistantGeminiApiKey");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                key = ReadJsonString(root, "InAppAssistantOpenAiApiKey");
            }

            if (string.IsNullOrWhiteSpace(key) || !LooksLikeOpenRouterKey(key))
            {
                return dto;
            }

            dto.InAppAssistantOpenRouterApiKey = key;
            var model = ReadJsonString(root, "InAppAssistantOpenRouterModel");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = ReadJsonString(root, "InAppAssistantGeminiModel");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                model = ReadJsonString(root, "InAppAssistantOpenAiModel");
            }

            dto.InAppAssistantOpenRouterModel = MapLegacyAssistantModel(model);
            return dto;
        }
        catch
        {
            return dto;
        }
    }

    private static string ReadJsonString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

    private static bool LooksLikeOpenRouterKey(string key) =>
        key.TrimStart().StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase);

    private static string MapLegacyAssistantModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)
            || model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            return "openrouter/free";
        }

        return model.Trim();
    }

    private static PlatformSettingsDto TryImportOpenRouterFromHermesWpf(PlatformSettingsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.InAppAssistantOpenRouterApiKey))
        {
            return dto;
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HermesWpf",
                "settings.json");
            if (!File.Exists(path))
            {
                return dto;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var key = ReadJsonString(root, "InAppAssistantOpenRouterApiKey");
            if (string.IsNullOrWhiteSpace(key) || !LooksLikeOpenRouterKey(key))
            {
                return dto;
            }

            dto.InAppAssistantOpenRouterApiKey = key;
            var model = ReadJsonString(root, "InAppAssistantOpenRouterModel");
            dto.InAppAssistantOpenRouterModel = MapLegacyAssistantModel(model);
        }
        catch
        {
            // ignore
        }

        return dto;
    }
}
