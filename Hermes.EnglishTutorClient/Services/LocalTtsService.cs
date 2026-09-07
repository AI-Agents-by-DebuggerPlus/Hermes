using System;
using System.Globalization;
using System.Speech.Synthesis;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Minimal SAPI TTS for question + words.</summary>
public sealed class LocalTtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private string _englishVoice = string.Empty;
    private string _russianVoice = string.Empty;

    public LocalTtsService()
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = -1;
    }

    public void ApplySettings(AppSettings s)
    {
        _englishVoice = s.EnglishVoiceName ?? string.Empty;
        _russianVoice = s.RussianVoiceName ?? string.Empty;
        _synth.Volume = Math.Max(0, Math.Min(100, s.VolumePercent));
    }

    public void Stop()
    {
        try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
    }

    public void Speak(string text, string lang)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Stop();
        var pb = new PromptBuilder();
        var culture = lang != null && lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("ru-RU")
            : new CultureInfo("en-US");
        var voice = lang != null && lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? _russianVoice
            : _englishVoice;

        AppendVoice(pb, voice, culture, text.Trim());
        _synth.SpeakAsync(pb);
    }

    public void SpeakSequence(System.Collections.Generic.IReadOnlyList<Hermes.EnglishTutorClient.Models.LangUtterance> parts)
    {
        if (parts == null || parts.Count == 0) return;
        Stop();
        var pb = new PromptBuilder();
        var first = true;
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p.Text)) continue;
            if (!first) pb.AppendBreak(PromptBreak.Medium);
            first = false;
            var culture = p.Lang != null && p.Lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? new CultureInfo("ru-RU")
                : new CultureInfo("en-US");
            var voice = p.Lang != null && p.Lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? _russianVoice
                : _englishVoice;
            AppendVoice(pb, voice, culture, p.Text.Trim());
        }

        if (!first)
            _synth.SpeakAsync(pb);
    }

    public void SpeakExercise(string question, string questionLang, string wordsJoined, string wordsLang)
    {
        Stop();
        var pb = new PromptBuilder();
        if (!string.IsNullOrWhiteSpace(question))
        {
            var qCulture = questionLang != null && questionLang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? new CultureInfo("ru-RU")
                : new CultureInfo("en-US");
            var qVoice = questionLang != null && questionLang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? _russianVoice
                : _englishVoice;
            AppendVoice(pb, qVoice, qCulture, question.Trim());
            pb.AppendBreak(PromptBreak.Medium);
        }

        if (!string.IsNullOrWhiteSpace(wordsJoined))
        {
            var wCulture = wordsLang != null && wordsLang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? new CultureInfo("ru-RU")
                : new CultureInfo("en-US");
            var wVoice = wordsLang != null && wordsLang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? _russianVoice
                : _englishVoice;
            AppendVoice(pb, wVoice, wCulture, wordsJoined.Trim());
        }

        _synth.SpeakAsync(pb);
    }

    private static void AppendVoice(PromptBuilder pb, string preferredName, CultureInfo culture, string text)
    {
        var info = ResolveVoice(preferredName, culture);
        if (info != null)
            pb.StartVoice(info);
        else
            pb.StartVoice(culture);
        pb.AppendText(text);
        pb.EndVoice();
    }

    private static VoiceInfo? ResolveVoice(string preferredName, CultureInfo culture)
    {
        using var probe = new SpeechSynthesizer();
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            foreach (var v in probe.GetInstalledVoices())
            {
                if (string.Equals(v.VoiceInfo.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                    return v.VoiceInfo;
            }
        }

        foreach (var v in probe.GetInstalledVoices(culture))
        {
            if (v.Enabled) return v.VoiceInfo;
        }

        foreach (var v in probe.GetInstalledVoices())
        {
            if (v.Enabled) return v.VoiceInfo;
        }

        return null;
    }

    public void Dispose() => _synth.Dispose();
}
