using System;
using System.IO;
using Newtonsoft.Json;

namespace Hermes.EnglishLearning.Services;

public sealed class AppSettings
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseAnonKey { get; set; } = string.Empty;
    public string RecipientName { get; set; } = "EnglishLearning";
    public string SenderNameFilter { get; set; } = "Hermes";
    public int PollSeconds { get; set; } = 8;
    public bool AutoSpeak { get; set; }
    public string LastLocalLessonPath { get; set; } = string.Empty;
}

public static class SettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesEnglishLearning",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, JsonSettings));
    }
}
