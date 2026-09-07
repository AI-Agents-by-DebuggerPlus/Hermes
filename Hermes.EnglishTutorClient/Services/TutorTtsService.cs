using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Routes TTS to SAPI or Azure (WAV via SoundPlayer).</summary>
public sealed class TutorTtsService : IDisposable
{
    private readonly LocalTtsService _sapi = new();
    private readonly AzureSpeechTtsClient _azure = new();
    private AppSettings _settings = new();
    private bool _useAzure;
    private CancellationTokenSource? _cts;
    private SoundPlayer? _player;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings ?? new AppSettings();
        _useAzure = string.Equals(_settings.TtsProvider, "Azure", StringComparison.OrdinalIgnoreCase)
                    && AzureSpeechTtsClient.IsConfigured(_settings);
        _sapi.ApplySettings(_settings);
        AppLog.Info("TTS: " + (_useAzure ? "Azure" : "SAPI"));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _sapi.Stop();
        try { _player?.Stop(); } catch { /* ignore */ }
    }

    public void Speak(string text, string lang)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Stop();
        if (_useAzure)
            _ = SpeakAzureAsync(text, lang);
        else
            _sapi.Speak(text, lang);
    }

    /// <summary>Speak a sequence of language-tagged fragments (ru/en) without speaking the keys.</summary>
    public void SpeakSequence(System.Collections.Generic.IReadOnlyList<Hermes.EnglishTutorClient.Models.LangUtterance> parts)
    {
        if (parts == null || parts.Count == 0) return;
        Stop();
        if (_useAzure)
            _ = SpeakAzureSequenceAsync(parts);
        else
            _sapi.SpeakSequence(parts);
    }

    public void SpeakExercise(string question, string questionLang, string wordsJoined, string wordsLang)
    {
        Stop();
        if (_useAzure)
        {
            _ = SpeakAzureExerciseAsync(question, questionLang, wordsJoined, wordsLang);
            return;
        }

        _sapi.SpeakExercise(question, questionLang, wordsJoined, wordsLang);
    }

    private async Task SpeakAzureAsync(string text, string lang)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var isRu = lang != null && lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
            var voice = isRu ? _settings.AzureRussianVoice : _settings.AzureEnglishVoice;
            var locale = isRu ? "ru-RU" : "en-US";
            var bytes = await _azure.SynthesizeAsync(_settings, text, voice, locale, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || bytes.Length == 0) return;
            PlayWav(bytes);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Azure TTS failed, fallback SAPI: " + ex.Message);
            _sapi.Speak(text, lang);
        }
    }

    private async Task SpeakAzureSequenceAsync(
        System.Collections.Generic.IReadOnlyList<Hermes.EnglishTutorClient.Models.LangUtterance> parts)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            foreach (var p in parts)
            {
                if (cts.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(p.Text)) continue;

                var isRu = p.Lang != null && p.Lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
                var voice = isRu ? _settings.AzureRussianVoice : _settings.AzureEnglishVoice;
                var locale = isRu ? "ru-RU" : "en-US";
                var bytes = await _azure.SynthesizeAsync(_settings, p.Text, voice, locale, cts.Token)
                    .ConfigureAwait(true);
                if (cts.IsCancellationRequested || bytes.Length == 0) continue;
                PlayWavSync(bytes);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Azure sequence TTS failed, fallback SAPI: " + ex.Message);
            _sapi.SpeakSequence(parts);
        }
    }

    private void PlayWavSync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "hermes_tutor_tts_" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(path, bytes);
        try
        {
            _player?.Stop();
            _player?.Dispose();
            _player = new SoundPlayer(path);
            _player.PlaySync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SoundPlayer sync: " + ex.Message);
        }
    }

    private async Task SpeakAzureExerciseAsync(string question, string qLang, string words, string wLang)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            if (!string.IsNullOrWhiteSpace(question))
            {
                await SpeakAzureAsync(question, qLang).ConfigureAwait(true);
                if (cts.IsCancellationRequested) return;
            }

            if (!string.IsNullOrWhiteSpace(words))
                await SpeakAzureAsync(words, wLang).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Azure exercise TTS failed: " + ex.Message);
            _sapi.SpeakExercise(question, qLang, words, wLang);
        }
    }

    private void PlayWav(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "hermes_tutor_tts_" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(path, bytes);
        try
        {
            _player?.Stop();
            _player?.Dispose();
            _player = new SoundPlayer(path);
            _player.Play();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SoundPlayer: " + ex.Message);
        }
    }

    public void Dispose()
    {
        Stop();
        _sapi.Dispose();
        try { _player?.Dispose(); } catch { /* ignore */ }
    }
}
