using System.IO;
using System.Reflection;
using System.Text.Json;
using Supabase;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>Publishes TradingPlatform startup to Supabase using Hermes.Wpf relay settings.</summary>
public static class SupabaseStartupNotifier
{
    private static int _published;

    public static void TryPublishOnStartup()
    {
        if (Interlocked.Exchange(ref _published, 1) != 0)
        {
            return;
        }

        _ = PublishCoreAsync();
    }

    private static async Task PublishCoreAsync()
    {
        try
        {
            if (!TryLoadHermesWpfSupabaseSettings(out var settings))
            {
                return;
            }

            if (!settings.RelayEnabled
                || string.IsNullOrWhiteSpace(settings.Url)
                || string.IsNullOrWhiteSpace(settings.AnonKey))
            {
                return;
            }

            var client = new Client(settings.Url.Trim(), settings.AnonKey.Trim());
            await client.InitializeAsync().ConfigureAwait(false);
            if (settings.UseAnonymousAuth)
            {
                if (client.Auth.CurrentUser is null)
                {
                    await client.Auth.SignInAnonymously().ConfigureAwait(false);
                }
            }

            var userId = client.Auth.CurrentUser?.Id;
            if (string.IsNullOrWhiteSpace(userId))
            {
                TradingPlatformFileLogger.Instance.Warn("[supabase] startup notify skipped — no auth session");
                return;
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["hermes_app"] = "trading_platform",
                ["event"] = "startup",
                ["version"] = version,
                ["timestamp"] = timestamp,
            });
            var voice = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["en"] = "Hermes Trading Platform started",
            });

            var sender = string.IsNullOrWhiteSpace(settings.HermesSenderName) ? "Hermes" : settings.HermesSenderName.Trim();
            var createdAt = settings.UseLocalCreatedAt ? DateTimeOffset.Now : DateTimeOffset.UtcNow;

            await InsertRowAsync(client, userId, sender, json, createdAt).ConfigureAwait(false);
            await InsertRowAsync(client, userId, sender, voice, createdAt).ConfigureAwait(false);
            TradingPlatformFileLogger.Instance.Info($"[supabase] TradingPlatform startup notification sent (v{version})");
        }
        catch (Exception ex)
        {
            TradingPlatformFileLogger.Instance.Warn($"[supabase] startup notify failed: {ex.Message}");
        }
    }

    private static async Task InsertRowAsync(Client client, string userId, string sender, string content, DateTimeOffset createdAt)
    {
        await client.From<SupabaseMessageInsertRow>().Insert(new SupabaseMessageInsertRow
        {
            SenderId = userId,
            SenderName = sender,
            Content = content,
            CreatedAt = createdAt,
        }).ConfigureAwait(false);
    }

    private static bool TryLoadHermesWpfSupabaseSettings(out HermesWpfSupabaseSettings settings)
    {
        settings = new HermesWpfSupabaseSettings();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HermesWpf",
                "settings.json");
            if (!File.Exists(path))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            settings.RelayEnabled = ReadBool(root, "SupabaseRelayEnabled");
            settings.Url = ReadString(root, "SupabaseUrl");
            settings.AnonKey = ReadString(root, "SupabaseAnonKey");
            settings.UseAnonymousAuth = ReadBool(root, "SupabaseUseAnonymousAuth", defaultValue: true);
            settings.UseLocalCreatedAt = ReadBool(root, "SupabaseUseLocalCreatedAt");
            settings.HermesSenderName = ReadString(root, "SupabaseHermesSenderName");
            if (string.IsNullOrWhiteSpace(settings.HermesSenderName))
            {
                settings.HermesSenderName = "Hermes";
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

    private static bool ReadBool(JsonElement root, string name, bool defaultValue = false)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return defaultValue;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }

    private sealed class HermesWpfSupabaseSettings
    {
        public bool RelayEnabled { get; set; }
        public string Url { get; set; } = string.Empty;
        public string AnonKey { get; set; } = string.Empty;
        public bool UseAnonymousAuth { get; set; } = true;
        public bool UseLocalCreatedAt { get; set; }
        public string HermesSenderName { get; set; } = "Hermes";
    }

    [Supabase.Postgrest.Attributes.Table("messages")]
    private sealed class SupabaseMessageInsertRow : Supabase.Postgrest.Models.BaseModel
    {
        [Supabase.Postgrest.Attributes.Column("sender_id")]
        public string SenderId { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("sender_name")]
        public string SenderName { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("content")]
        public string Content { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
