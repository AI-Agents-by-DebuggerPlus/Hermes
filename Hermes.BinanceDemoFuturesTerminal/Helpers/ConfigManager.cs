using System.IO;
using System.Text.Json;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.Services;

namespace Hermes.BinanceDemoFuturesTerminal.Helpers;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static PlatformSettings LoadSettings()
    {
        try
        {
            var path = TerminalPaths.SettingsFile;
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PlatformSettings>(File.ReadAllText(path)) ?? new PlatformSettings();
            }

            // migrate legacy credentials from LocalAppData
            var legacy = LoadLegacyCredentials();
            if (!string.IsNullOrEmpty(legacy.ApiKey))
            {
                var migrated = new PlatformSettings
                {
                    ApiKey = legacy.ApiKey,
                    SecretKey = legacy.SecretKey,
                };
                SaveSettings(migrated);
                return migrated;
            }
        }
        catch
        {
            // ignore
        }

        return new PlatformSettings();
    }

    public static void SaveSettings(PlatformSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TerminalPaths.SettingsFile)!);
            File.WriteAllText(TerminalPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    private static (string ApiKey, string SecretKey) LoadLegacyCredentials()
    {
        try
        {
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesBinanceDemoFutures",
                "api_credentials.json");
            if (!File.Exists(legacyPath))
            {
                return (string.Empty, string.Empty);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = doc.RootElement;
            return (
                root.TryGetProperty("ApiKey", out var k) ? k.GetString() ?? "" : "",
                root.TryGetProperty("SecretKey", out var s) ? s.GetString() ?? "" : "");
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }
}
