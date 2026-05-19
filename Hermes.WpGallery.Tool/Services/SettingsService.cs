using System.IO;
using System.Text.Json;
using Hermes.WpGallery.Tool.Models;

namespace Hermes.WpGallery.Tool.Services;

public class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Hermes.WpGallery.Tool", "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public SettingsService() => Load();

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                MigrateWordPressUrl();
                MigrateChannel();
            }
        }
        catch { Current = new AppSettings(); }
    }

    private void MigrateWordPressUrl()
    {
        var url = Current.WordPressUrl?.Trim() ?? "";
        if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var host = new Uri(url).Host;
            Current.WordPressUrl = $"https://{host}";
            Save();
        }
        catch { /* оставить как есть — WordPressService покажет ошибку */ }
    }

    private void MigrateChannel()
    {
        if (string.IsNullOrWhiteSpace(Current.Channel))
        {
            Current.Channel = "camera1";
            Save();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, _opts));
    }
}
