using System;
using System.IO;
using Newtonsoft.Json;

namespace Hermes.EnglishLearning.Xp;

internal sealed class AppSettings
{
    public string SupabaseUrl { get; set; } = "https://dauvhkttddxmqfkfunqg.supabase.co";
    public string SupabaseAnonKey { get; set; } = string.Empty;
    public string RecipientName { get; set; } = "EnglishLearning";
    public int PollSeconds { get; set; } = 5;
    public int WordColumns { get; set; } = 2;
    public int CardsPerScreenWords { get; set; } = 8;
    public int CardsPerScreenOther { get; set; } = 3;
    public string LessonsFolder { get; set; } = string.Empty;

    /// <summary>UI text scale (Ctrl+/-). 1.0 = default. Persisted.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>Supabase recipient for page TTS (Android Speak).</summary>
    public string TtsRecipientName { get; set; } = "AndroidChat";

    /// <summary>Supabase sender_name when publishing page TTS.</summary>
    public string TtsSenderName { get; set; } = "EnglishLearning";
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
        if (s.WordColumns < 1) s.WordColumns = 1;
        if (s.WordColumns > 3) s.WordColumns = 3;
        if (s.CardsPerScreenWords < 2) s.CardsPerScreenWords = 2;
        if (s.CardsPerScreenOther < 1) s.CardsPerScreenOther = 1;
        if (string.IsNullOrWhiteSpace(s.RecipientName)) s.RecipientName = "EnglishLearning";
        if (string.IsNullOrWhiteSpace(s.TtsRecipientName)) s.TtsRecipientName = "AndroidChat";
        if (string.IsNullOrWhiteSpace(s.TtsSenderName)) s.TtsSenderName = "EnglishLearning";
        if (s.UiScale < 0.6) s.UiScale = 0.6;
        if (s.UiScale > 2.5) s.UiScale = 2.5;
        // Snap lightly to avoid float junk in JSON
        s.UiScale = Math.Round(s.UiScale, 2);
    }

    public static string ResolveLessonsFolder(AppSettings s)
    {
        var dir = string.IsNullOrWhiteSpace(s.LessonsFolder)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lessons")
            : s.LessonsFolder.Trim();
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        return dir;
    }
}
