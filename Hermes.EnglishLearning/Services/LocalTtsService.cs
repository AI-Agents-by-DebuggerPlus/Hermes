using System;
using System.Globalization;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>Local TTS via Windows SAPI. Order per card: EN → RU → EN → EN.</summary>
public sealed class LocalTtsService : IDisposable
{
    private static readonly Regex SlashSplit = new(@"\s*/\s*", RegexOptions.Compiled);

    private readonly SpeechSynthesizer _synth = new();
    private bool _paused;
    private bool _hasActiveUtterance;

    public event EventHandler? SpeakCompleted;

    public LocalTtsService()
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = -1;
        _synth.SpeakCompleted += (_, e) =>
        {
            _paused = false;
            // Cancelled prompts should not count as "finished screen"
            var finishedNaturally = !e.Cancelled;
            _hasActiveUtterance = false;
            if (finishedNaturally)
            {
                SpeakCompleted?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public bool IsSpeaking => _synth.State == SynthesizerState.Speaking;
    public bool IsPaused => _paused || _synth.State == SynthesizerState.Paused;
    public bool HasActiveOrPausedUtterance =>
        _hasActiveUtterance || _synth.State == SynthesizerState.Speaking || _synth.State == SynthesizerState.Paused;

    private string _englishVoiceName = string.Empty;
    private string _russianVoiceName = string.Empty;

    public void ApplyVoices(string? englishVoiceName, string? russianVoiceName)
    {
        _englishVoiceName = englishVoiceName ?? string.Empty;
        _russianVoiceName = russianVoiceName ?? string.Empty;
    }

    public void SetVolumePercent(int percent)
    {
        var p = Math.Max(0, Math.Min(100, percent));
        _synth.Volume = p;
    }

    /// <summary>Prime synthesizer so OS may route media keys earlier.</summary>
    public void WarmUp()
    {
        try
        {
            _synth.Volume = _synth.Volume;
            AppLog.Info("TTS warm-up OK, voices available via SAPI");
        }
        catch (Exception ex)
        {
            AppLog.Warn("TTS warm-up: " + ex.Message);
        }
    }

    public void SpeakScreen(LessonScreen screen)
    {
        if (screen?.Cards == null || screen.Cards.Count == 0)
        {
            return;
        }

        Stop();
        var pb = new PromptBuilder();
        foreach (var card in screen.Cards)
        {
            AppendCardSequence(pb, card);
            pb.AppendBreak(PromptBreak.Medium);
        }

        _hasActiveUtterance = true;
        _synth.SpeakAsync(pb);
        AppLog.Info("TTS speak screen: " + screen.SectionLabel + " cards=" + screen.Cards.Count);
    }

    /// <summary>
    /// Returns true if pause/resume handled; false if idle (caller may go to next screen).
    /// </summary>
    public bool TryTogglePause()
    {
        if (_synth.State == SynthesizerState.Speaking)
        {
            _synth.Pause();
            _paused = true;
            AppLog.Info("TTS paused");
            return true;
        }

        if (_synth.State == SynthesizerState.Paused || _paused)
        {
            _synth.Resume();
            _paused = false;
            AppLog.Info("TTS resumed");
            return true;
        }

        return false;
    }

    public void Stop()
    {
        try
        {
            if (_synth.State == SynthesizerState.Paused)
            {
                _synth.Resume();
            }
        }
        catch
        {
        }

        _synth.SpeakAsyncCancelAll();
        _paused = false;
        _hasActiveUtterance = false;
    }

    private void AppendCardSequence(PromptBuilder pb, CardPair card)
    {
        AppendSpoken(pb, card.En, english: true);
        AppendSpoken(pb, card.Ru, english: false);
        AppendSpoken(pb, card.En, english: true);
        AppendSpoken(pb, card.En, english: true);
    }

    private void AppendSpoken(PromptBuilder pb, string text, bool english)
    {
        var normalized = TtsUtterancePlanner.NormalizeForSpeech(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var culture = english ? "en-US" : "ru-RU";
        var voiceName = english ? _englishVoiceName : _russianVoiceName;
        StartVoice(pb, culture, voiceName);

        var parts = SlashSplit.Split(normalized);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Length == 0)
            {
                continue;
            }

            pb.AppendText(part);
            if (i < parts.Length - 1)
            {
                pb.AppendBreak(PromptBreak.Medium);
            }
        }

        EndVoice(pb);
        pb.AppendBreak(PromptBreak.Small);
    }

    private static void StartVoice(PromptBuilder pb, string culture, string voiceName)
    {
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            try
            {
                using var probe = new SpeechSynthesizer();
                foreach (InstalledVoice v in probe.GetInstalledVoices())
                {
                    if (v.Enabled && string.Equals(v.VoiceInfo.Name, voiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        pb.StartVoice(v.VoiceInfo);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            pb.StartVoice(new CultureInfo(culture));
        }
        catch
        {
        }
    }

    private static void EndVoice(PromptBuilder pb)
    {
        try
        {
            pb.EndVoice();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Stop();
        _synth.Dispose();
    }
}
