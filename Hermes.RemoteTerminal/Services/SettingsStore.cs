using System.IO;
using System.Text.Json;
using Hermes.RemoteTerminal.Models;

namespace Hermes.RemoteTerminal.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Always beside the executable.</summary>
    public static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

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
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
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
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));
    }

    public static void Normalize(AppSettings s)
    {
        if (s.MaxLines < 50) s.MaxLines = 50;
        if (s.MaxLines > 5000) s.MaxLines = 5000;
        if (string.IsNullOrWhiteSpace(s.RecipientName)) s.RecipientName = "RemoteTerminal";
        if (string.IsNullOrWhiteSpace(s.LocalSenderName)) s.LocalSenderName = "RemoteTerminal";
        if (s.RestBackupPollSeconds < 3) s.RestBackupPollSeconds = 3;
        if (s.RestBackupPollSeconds > 120) s.RestBackupPollSeconds = 120;
        s.Ui ??= UiThemeSettings.FromScheme("DarkBinance");
        if (string.IsNullOrWhiteSpace(s.Ui.ColorScheme))
        {
            s.Ui.ColorScheme = "DarkBinance";
        }

        s.Ui.ClampFonts();
    }
}
