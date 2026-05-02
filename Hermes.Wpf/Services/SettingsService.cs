using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "HermesWpf");
        Directory.CreateDirectory(root);
        _settingsFilePath = Path.Combine(root, "settings.json");
    }

    public async Task<HermesSettings> LoadAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            var defaults = new HermesSettings();
            await SaveAsync(defaults);
            return defaults;
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        var settings = await JsonSerializer.DeserializeAsync<HermesSettings>(stream) ?? new HermesSettings();
        settings.SavedProjectPaths ??= [];
        settings.VisionScopeReminderNote ??= string.Empty;
        settings.WorkspaceRootWindowsPath ??= string.Empty;
        settings.LastWorkspaceBrowsePath ??= string.Empty;
        settings.SupabaseUrl ??= string.Empty;
        settings.SupabaseAnonKey ??= string.Empty;
        settings.SupabaseHermesSenderName = string.IsNullOrWhiteSpace(settings.SupabaseHermesSenderName)
            ? "Hermes"
            : settings.SupabaseHermesSenderName.Trim();
        settings.SupabaseLocalSenderName = string.IsNullOrWhiteSpace(settings.SupabaseLocalSenderName)
            ? "Desktop"
            : settings.SupabaseLocalSenderName.Trim();
        settings.SupabasePollIntervalSeconds =
            Math.Clamp(settings.SupabasePollIntervalSeconds, 1, 120);
        if (settings.ChatFontSize is < 8 or > 36 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
        {
            settings.ChatFontSize = 14;
        }

        return settings;
    }

    public async Task SaveAsync(HermesSettings settings)
    {
        settings.SupabasePollIntervalSeconds = Math.Clamp(settings.SupabasePollIntervalSeconds, 1, 120);
        if (string.IsNullOrWhiteSpace(settings.SupabaseHermesSenderName))
        {
            settings.SupabaseHermesSenderName = "Hermes";
        }
        else
        {
            settings.SupabaseHermesSenderName = settings.SupabaseHermesSenderName.Trim();
        }

        if (string.IsNullOrWhiteSpace(settings.SupabaseLocalSenderName))
        {
            settings.SupabaseLocalSenderName = "Desktop";
        }
        else
        {
            settings.SupabaseLocalSenderName = settings.SupabaseLocalSenderName.Trim();
        }

        await using var stream = File.Create(_settingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
    }
}
