using System;
using System.IO;
using System.Speech.Synthesis;
using Newtonsoft.Json;

namespace Hermes.EnglishLearning.Services;

public sealed class AppSettings
{
    // Defaults travel with the app folder (portable).
    public string SupabaseUrl { get; set; } = "https://dauvhkttddxmqfkfunqg.supabase.co";
    public string SupabaseAnonKey { get; set; } = "sb_publishable_D1-ieyE_Tskl6BUrOSJ7RA_x-9spRcz";
    public string RecipientName { get; set; } = "EnglishLearning";
    public string SenderNameFilter { get; set; } = "Hermes";
    public int PollSeconds { get; set; } = 8;
    public bool AutoSpeak { get; set; }

    public double EnglishFontSize { get; set; } = 42;
    public double RussianFontSize { get; set; } = 28;
    public string EnglishColor { get; set; } = "#F8D12F";
    public string RussianColor { get; set; } = "#D0D4DC";

    /// <summary>Sapi or Azure.</summary>
    public string TtsProvider { get; set; } = "Azure";

    /// <summary>Installed SAPI voice name; empty = culture default.</summary>
    public string EnglishVoiceName { get; set; } = string.Empty;
    public string RussianVoiceName { get; set; } = string.Empty;

    /// <summary>1–3 columns for the words section.</summary>
    public int WordColumns { get; set; } = 2;

    public string HotkeyNext { get; set; } = "Right";
    public string HotkeyPrev { get; set; } = "Left";
    public string HotkeySpeak { get; set; } = "S";
    public string HotkeyFullscreen { get; set; } = "F";
    public string HotkeyStop { get; set; } = "Escape";

    public int VolumePercent { get; set; } = 80;

    public string LastLocalLessonPath { get; set; } = string.Empty;

    /// <summary>0-based screen index within LastLocalLessonPath (restored on restart).</summary>
    public int LastScreenIndex { get; set; }

    /// <summary>Azure AI multi-service / Speech key (same key as Vision when multi-service).</summary>
    public string AzureSpeechKey { get; set; } = string.Empty;

    /// <summary>Custom subdomain endpoint, e.g. https://name.cognitiveservices.azure.com/</summary>
    public string AzureSpeechEndpoint { get; set; } = string.Empty;

    /// <summary>Optional region (eastus, westeurope). Needed if using regional TTS host.</summary>
    public string AzureSpeechRegion { get; set; } = string.Empty;

    public string AzureEnglishVoice { get; set; } = "en-US-JennyNeural";
    public string AzureRussianVoice { get; set; } = "ru-RU-SvetlanaNeural";
}

public static class SettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>settings.json next to the EXE — portable with the app folder.</summary>
    public static string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings) ?? new AppSettings();
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
        if (s.EnglishFontSize < 12)
        {
            s.EnglishFontSize = 12;
        }

        if (s.EnglishFontSize > 96)
        {
            s.EnglishFontSize = 96;
        }

        if (s.RussianFontSize < 10)
        {
            s.RussianFontSize = 10;
        }

        if (s.RussianFontSize > 80)
        {
            s.RussianFontSize = 80;
        }

        if (s.WordColumns < 1)
        {
            s.WordColumns = 1;
        }

        if (s.WordColumns > 3)
        {
            s.WordColumns = 3;
        }

        if (s.PollSeconds < 3)
        {
            s.PollSeconds = 3;
        }

        if (string.IsNullOrWhiteSpace(s.EnglishColor))
        {
            s.EnglishColor = "#F8D12F";
        }

        if (string.IsNullOrWhiteSpace(s.RussianColor))
        {
            s.RussianColor = "#D0D4DC";
        }

        if (string.IsNullOrWhiteSpace(s.RecipientName))
        {
            s.RecipientName = "EnglishLearning";
        }

        if (string.IsNullOrWhiteSpace(s.HotkeyFullscreen))
        {
            s.HotkeyFullscreen = "F";
        }

        if (string.IsNullOrWhiteSpace(s.HotkeySpeak))
        {
            s.HotkeySpeak = "S";
        }

        if (s.LastScreenIndex < 0)
        {
            s.LastScreenIndex = 0;
        }

        if (string.IsNullOrWhiteSpace(s.TtsProvider))
        {
            s.TtsProvider = string.IsNullOrWhiteSpace(s.AzureSpeechKey) ? "Sapi" : "Azure";
        }

        if (string.IsNullOrWhiteSpace(s.AzureEnglishVoice))
        {
            s.AzureEnglishVoice = "en-US-JennyNeural";
        }

        if (string.IsNullOrWhiteSpace(s.AzureRussianVoice))
        {
            s.AzureRussianVoice = "ru-RU-SvetlanaNeural";
        }

        if (s.VolumePercent < 0)
        {
            s.VolumePercent = 0;
        }

        if (s.VolumePercent > 100)
        {
            s.VolumePercent = 100;
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
                if (v.Enabled)
                {
                    list.Add(v.VoiceInfo);
                }
            }

            return list.ToArray();
        }
        catch
        {
            return Array.Empty<VoiceInfo>();
        }
    }
}
