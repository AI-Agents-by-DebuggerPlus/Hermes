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

    public string SettingsFilePath => _settingsFilePath;

    public async Task<HermesSettings> LoadAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return await CreateAndSaveDefaultsAsync();
        }

        var raw = await File.ReadAllTextAsync(_settingsFilePath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            BackupCorruptSettingsFile("empty");
            return await CreateAndSaveDefaultsAsync();
        }

        HermesSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<HermesSettings>(raw) ?? new HermesSettings();
        }
        catch (JsonException)
        {
            BackupCorruptSettingsFile("invalid-json");
            return await CreateAndSaveDefaultsAsync();
        }

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
        settings.ExternalBrainOllamaBaseUrl = string.IsNullOrWhiteSpace(settings.ExternalBrainOllamaBaseUrl)
            ? "http://127.0.0.1:11434"
            : settings.ExternalBrainOllamaBaseUrl.Trim();
        settings.ExternalBrainEmbeddingModel = string.IsNullOrWhiteSpace(settings.ExternalBrainEmbeddingModel)
            ? "nomic-embed-text"
            : settings.ExternalBrainEmbeddingModel.Trim();
        settings.GeneratedSkillsDirectory ??= string.Empty;
        settings.SkillMaxGenerationAttempts = Math.Clamp(settings.SkillMaxGenerationAttempts, 1, 10);
        settings.SkillSandboxTimeoutSeconds = Math.Clamp(settings.SkillSandboxTimeoutSeconds, 5, 300);
        settings.SkillResolveMaxSuggestions = Math.Clamp(settings.SkillResolveMaxSuggestions, 1, 8);
        settings.SkillResolveMinScore = Math.Clamp(settings.SkillResolveMinScore, 0.1, 0.9);
        if (settings.ChatFontSize is < 8 or > 36 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
        {
            settings.ChatFontSize = 14;
        }

        MigrateHermesGallerySettings(settings);
        var migrated = MigrateSupabaseFromDesktopVoiceChat(settings);
        if (MigrateInAppAssistantLegacyKeys(settings, raw))
        {
            migrated = true;
        }

        if (migrated)
        {
            await SaveAsync(settings);
        }

        return settings;
    }

    public async Task SaveAsync(HermesSettings settings)
    {
        settings.ExternalBrainMemoryPath ??= string.Empty;
        settings.ExternalBrainMaxContextItems = Math.Clamp(settings.ExternalBrainMaxContextItems, 1, 20);
        settings.ExternalBrainOllamaBaseUrl = string.IsNullOrWhiteSpace(settings.ExternalBrainOllamaBaseUrl)
            ? "http://127.0.0.1:11434"
            : settings.ExternalBrainOllamaBaseUrl.Trim();
        settings.ExternalBrainEmbeddingModel = string.IsNullOrWhiteSpace(settings.ExternalBrainEmbeddingModel)
            ? "nomic-embed-text"
            : settings.ExternalBrainEmbeddingModel.Trim();
        settings.GeneratedSkillsDirectory ??= string.Empty;
        settings.SkillMaxGenerationAttempts = Math.Clamp(settings.SkillMaxGenerationAttempts, 1, 10);
        settings.SkillSandboxTimeoutSeconds = Math.Clamp(settings.SkillSandboxTimeoutSeconds, 5, 300);
        settings.SkillResolveMaxSuggestions = Math.Clamp(settings.SkillResolveMaxSuggestions, 1, 8);
        settings.SkillResolveMinScore = Math.Clamp(settings.SkillResolveMinScore, 0.1, 0.9);
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

        var tempPath = _settingsFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
        }

        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    private async Task<HermesSettings> CreateAndSaveDefaultsAsync()
    {
        var defaults = new HermesSettings();
        await SaveAsync(defaults);
        return defaults;
    }

    private void BackupCorruptSettingsFile(string reason)
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{_settingsFilePath}.{reason}.{stamp}.bak";
            File.Copy(_settingsFilePath, backupPath, overwrite: true);
        }
        catch
        {
            // non-fatal
        }
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

    /// <summary>
    /// Copies Supabase URL/anon key from DesktopVoiceChat when Hermes settings are empty
    /// (same project family; path: %LocalAppData%\DesktopVoiceChat\settings.json).
    /// </summary>
    private static bool MigrateSupabaseFromDesktopVoiceChat(HermesSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.SupabaseUrl) && !string.IsNullOrWhiteSpace(s.SupabaseAnonKey))
        {
            return false;
        }

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopVoiceChat",
            "settings.json");
        if (!File.Exists(legacyPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = doc.RootElement;
            var url = ReadJsonString(root, "SupabaseUrl");
            var key = ReadJsonString(root, "SupabaseAnonKey");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var changed = false;
            if (string.IsNullOrWhiteSpace(s.SupabaseUrl))
            {
                s.SupabaseUrl = url.Trim();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(s.SupabaseAnonKey))
            {
                s.SupabaseAnonKey = key.Trim();
                changed = true;
            }

            if (string.Equals(s.SupabaseLocalSenderName, "Desktop", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("SenderName", out var senderEl))
            {
                var sender = (senderEl.GetString() ?? string.Empty).Trim();
                if (sender.Length > 0)
                {
                    s.SupabaseLocalSenderName = sender;
                    changed = true;
                }
            }

            if (root.TryGetProperty("UseAnonymousSession", out var anonEl)
                && anonEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                s.SupabaseUseAnonymousAuth = anonEl.GetBoolean();
                changed = true;
            }

            return changed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Migrates legacy assistant fields (OpenAI/Gemini) to OpenRouter.</summary>
    private static bool MigrateInAppAssistantLegacyKeys(HermesSettings s, string rawJson)
    {
        if (!string.IsNullOrWhiteSpace(s.InAppAssistantOpenRouterApiKey))
        {
            return false;
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
                return false;
            }

            s.InAppAssistantOpenRouterApiKey = key;
            var model = ReadJsonString(root, "InAppAssistantOpenRouterModel");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = ReadJsonString(root, "InAppAssistantGeminiModel");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                model = ReadJsonString(root, "InAppAssistantOpenAiModel");
            }

            s.InAppAssistantOpenRouterModel = MapLegacyAssistantModel(model);
            return true;
        }
        catch
        {
            return false;
        }
    }

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

    private static string ReadJsonString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

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
