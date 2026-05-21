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
            return new PlatformSettingsDto();
        }

        try
        {
            return JsonSerializer.Deserialize<PlatformSettingsDto>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new PlatformSettingsDto();
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
}
