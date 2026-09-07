using System;
using System.IO;
using Newtonsoft.Json;

namespace Hermes.RemoteTerminal.Xp;

internal sealed class AppSettings
{
    public string SupabaseUrl { get; set; } = "https://dauvhkttddxmqfkfunqg.supabase.co";
    public string SupabaseAnonKey { get; set; } = string.Empty;

    /// <summary>Only rows with this recipient_name (unless ShowAllRecipients).</summary>
    public string RecipientName { get; set; } = "RemoteTerminal";

    public int PollSeconds { get; set; } = 5;
    public int MaxLines { get; set; } = 500;

    /// <summary>If true, show every new message (debug). Default: filter by RecipientName.</summary>
    public bool ShowAllRecipients { get; set; }
}

internal static class SettingsStore
{
    public static string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var d = new AppSettings();
                Save(d);
                return d;
            }

            var json = File.ReadAllText(SettingsPath);
            var s = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            Normalize(s);
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings s)
    {
        Normalize(s);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(s, Formatting.Indented));
    }

    public static void Normalize(AppSettings s)
    {
        if (s.PollSeconds < 3) s.PollSeconds = 3;
        if (s.PollSeconds > 60) s.PollSeconds = 60;
        if (s.MaxLines < 50) s.MaxLines = 50;
        if (s.MaxLines > 5000) s.MaxLines = 5000;
        if (string.IsNullOrWhiteSpace(s.RecipientName)) s.RecipientName = "RemoteTerminal";
    }
}
