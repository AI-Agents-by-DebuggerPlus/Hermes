using System.IO;
using System.Text.Json;
using Hermes.WpGallery;
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
        settings.ExternalBrainMemoryPath ??= string.Empty;
        settings.ExternalBrainMaxContextItems =
            Math.Clamp(settings.ExternalBrainMaxContextItems, 1, 20);
        if (settings.ChatFontSize is < 8 or > 36 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
        {
            settings.ChatFontSize = 14;
        }

        MigrateHermesGallerySettings(settings);

        return settings;
    }

    public async Task SaveAsync(HermesSettings settings)
    {
        settings.ExternalBrainMemoryPath ??= string.Empty;
        settings.ExternalBrainMaxContextItems = Math.Clamp(settings.ExternalBrainMaxContextItems, 1, 20);
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

    private static void MigrateHermesGallerySettings(HermesSettings s)
    {
        s.HermesGallerySiteUrl ??= string.Empty;
        s.HermesGalleryRestUrl ??= string.Empty;
        s.HermesGalleryWebSocketUrl ??= string.Empty;
        s.HermesGalleryToken ??= string.Empty;
        s.HermesGalleryChannel = string.IsNullOrWhiteSpace(s.HermesGalleryChannel)
            ? "default"
            : s.HermesGalleryChannel.Trim();
        s.HermesGalleryMaxRetries = Math.Clamp(s.HermesGalleryMaxRetries, 1, 10);

        NormalizeHermesGallerySiteUrl(s);

        if (!s.HermesGalleryPublishEnabled && s.WordPressScreenshotPublishEnabled)
        {
            s.HermesGalleryPublishEnabled = true;
        }

        // Site configured but auto-publish off — likely old default (false); enable when URL is set.
        if (!s.HermesGalleryPublishEnabled
            && WpGalleryEndpoints.TryNormalizeSiteUrl(s.HermesGallerySiteUrl, out _, out _))
        {
            s.HermesGalleryPublishEnabled = true;
        }

        if (string.IsNullOrWhiteSpace(s.HermesGalleryToken)
            && !string.IsNullOrWhiteSpace(s.WordPressScreenshotApiKey))
        {
            s.HermesGalleryToken = s.WordPressScreenshotApiKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(s.HermesGallerySiteUrl))
        {
            if (!string.IsNullOrWhiteSpace(s.WordPressSiteUrl))
            {
                s.HermesGallerySiteUrl = s.WordPressSiteUrl.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(s.HermesGalleryRestUrl)
                     && WpGalleryEndpoints.TryNormalizeSiteUrl(
                         s.HermesGalleryRestUrl, out var site, out _))
            {
                s.HermesGallerySiteUrl = site;
            }
        }
    }

    private static void NormalizeHermesGallerySiteUrl(HermesSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.HermesGallerySiteUrl))
        {
            return;
        }

        if (WpGalleryEndpoints.TryNormalizeSiteUrl(s.HermesGallerySiteUrl.Trim(), out var site, out _))
        {
            s.HermesGallerySiteUrl = site;
        }
    }
}
