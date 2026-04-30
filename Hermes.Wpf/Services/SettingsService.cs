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
        return settings;
    }

    public async Task SaveAsync(HermesSettings settings)
    {
        await using var stream = File.Create(_settingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
    }
}
