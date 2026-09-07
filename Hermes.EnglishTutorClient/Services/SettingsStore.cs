using System;
using System.IO;
using System.Speech.Synthesis;
using Newtonsoft.Json;

namespace Hermes.EnglishTutorClient.Services;

public sealed class AppSettings
{
    public bool PreferRemoteWhenConnected { get; set; } = true;

    public string SupabaseUrl { get; set; } = "https://dauvhkttddxmqfkfunqg.supabase.co";
    public string SupabaseAnonKey { get; set; } = "sb_publishable_D1-ieyE_Tskl6BUrOSJ7RA_x-9spRcz";
    public string RecipientName { get; set; } = "EnglishTutorClient";
    public string HermesRecipientName { get; set; } = "Hermes";
    public string SenderNameFilter { get; set; } = "Hermes";
    public int PollSeconds { get; set; } = 5;

    public double TutorFontSize { get; set; } = 28;
    public double UserFontSize { get; set; } = 18;
    public double QuestionFontSize { get; set; } = 16;
    public double WordsFontSize { get; set; } = 40;
    public string TutorColor { get; set; } = "#F8D12F";
    public string UserColor { get; set; } = "#EAECEF";
    public string QuestionColor { get; set; } = "#848E9C";
    public string WordsColor { get; set; } = "#F8D12F";

    public string TtsProvider { get; set; } = "Azure";
    public string EnglishVoiceName { get; set; } = string.Empty;
    public string RussianVoiceName { get; set; } = string.Empty;
    public string AzureSpeechKey { get; set; } = string.Empty;
    public string AzureSpeechEndpoint { get; set; } = string.Empty;
    public string AzureSpeechRegion { get; set; } = string.Empty;
    public string AzureEnglishVoice { get; set; } = "en-US-JennyNeural";
    public string AzureRussianVoice { get; set; } = "ru-RU-SvetlanaNeural";
    public string GoogleSpeechApiKey { get; set; } = string.Empty;
    public int VolumePercent { get; set; } = 85;
    public bool AutoSpeak { get; set; } = true;

    public string HotkeyFullscreen { get; set; } = "F11";
    public string HotkeySend { get; set; } = "Enter";
    public string HotkeyNewline { get; set; } = "Shift+Enter";
    public string HotkeySpeak { get; set; } = "Ctrl+S";
    public string HotkeyStartTest { get; set; } = "Ctrl+T";
    public string HotkeyStopTts { get; set; } = "Escape";
    public string HotkeyOpenSettings { get; set; } = "Ctrl+,";
    public string HotkeyOpenLog { get; set; } = "Ctrl+L";
    public string HotkeyFocusInput { get; set; } = "Ctrl+I";
    public string HotkeyClearChat { get; set; } = ""; // stub
    public string HotkeyToggleVoiceInput { get; set; } = "MediaPlayPause";

    public string LogFolder { get; set; } = string.Empty;
}

public static class SettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var loaded = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath), JsonSettings)
                         ?? new AppSettings();
            Normalize(loaded);
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Normalize(settings);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, JsonSettings));
    }

    public static void Normalize(AppSettings s)
    {
        if (s.TutorFontSize < 12) s.TutorFontSize = 12;
        if (s.TutorFontSize > 72) s.TutorFontSize = 72;
        if (s.UserFontSize < 10) s.UserFontSize = 10;
        if (s.UserFontSize > 48) s.UserFontSize = 48;
        if (s.QuestionFontSize < 10) s.QuestionFontSize = 10;
        if (s.QuestionFontSize > 48) s.QuestionFontSize = 48;
        if (s.WordsFontSize < 16) s.WordsFontSize = 16;
        if (s.WordsFontSize > 96) s.WordsFontSize = 96;
        if (s.VolumePercent < 0) s.VolumePercent = 0;
        if (s.VolumePercent > 100) s.VolumePercent = 100;
        if (s.PollSeconds < 3) s.PollSeconds = 3;
        if (s.PollSeconds > 60) s.PollSeconds = 60;
        if (string.IsNullOrWhiteSpace(s.RecipientName)) s.RecipientName = "EnglishTutorClient";
        if (string.IsNullOrWhiteSpace(s.HermesRecipientName)) s.HermesRecipientName = "Hermes";
        if (string.IsNullOrWhiteSpace(s.SupabaseUrl))
            s.SupabaseUrl = "https://dauvhkttddxmqfkfunqg.supabase.co";
        if (string.IsNullOrWhiteSpace(s.SupabaseAnonKey))
            s.SupabaseAnonKey = "sb_publishable_D1-ieyE_Tskl6BUrOSJ7RA_x-9spRcz";
        if (string.IsNullOrWhiteSpace(s.TtsProvider))
            s.TtsProvider = string.IsNullOrWhiteSpace(s.AzureSpeechKey) ? "Sapi" : "Azure";
        if (string.IsNullOrWhiteSpace(s.AzureEnglishVoice)) s.AzureEnglishVoice = "en-US-JennyNeural";
        if (string.IsNullOrWhiteSpace(s.AzureRussianVoice)) s.AzureRussianVoice = "ru-RU-SvetlanaNeural";
        if (string.IsNullOrWhiteSpace(s.HotkeyFullscreen)) s.HotkeyFullscreen = "F11";
        // Always keep logs under Hermes.EnglishTutorClient/logs (migrate off LocalAppData).
        var projectLogs = AppLog.ResolveDefaultLogFolder();
        if (string.IsNullOrWhiteSpace(s.LogFolder)
            || s.LogFolder.IndexOf("Local\\Hermes.EnglishTutorClient", StringComparison.OrdinalIgnoreCase) >= 0
            || s.LogFolder.IndexOf("Local/Hermes.EnglishTutorClient", StringComparison.OrdinalIgnoreCase) >= 0
            || s.LogFolder.IndexOf("LocalApplicationData", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            s.LogFolder = projectLogs;
        }
    }

    public static VoiceInfo[] ListInstalledVoices()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var list = new System.Collections.Generic.List<VoiceInfo>();
            foreach (InstalledVoice v in synth.GetInstalledVoices())
            {
                if (v.Enabled) list.Add(v.VoiceInfo);
            }
            return list.ToArray();
        }
        catch
        {
            return Array.Empty<VoiceInfo>();
        }
    }
}
