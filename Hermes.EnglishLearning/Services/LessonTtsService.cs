using System;
using System.Threading;
using System.Threading.Tasks;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>Routes speak/pause to SAPI or Azure+cache based on settings.</summary>
public sealed class LessonTtsService : IDisposable
{
    private readonly LocalTtsService _sapi = new();
    private readonly AzureCachedTtsPlayer _azure = new();
    private AppSettings _settings = new();
    private bool _useAzure;

    public LessonTtsService()
    {
        _sapi.SpeakCompleted += (_, __) => SpeakCompleted?.Invoke(this, EventArgs.Empty);
        _azure.SpeakCompleted += (_, __) => SpeakCompleted?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SpeakCompleted;

    public bool IsSpeaking => _useAzure ? _azure.IsSpeaking : _sapi.IsSpeaking;
    public bool IsPaused => _useAzure ? _azure.IsPaused : _sapi.IsPaused;
    public bool HasActiveOrPausedUtterance =>
        _useAzure ? _azure.HasActiveOrPausedUtterance : _sapi.HasActiveOrPausedUtterance;

    public bool UsesAzure => _useAzure;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings ?? new AppSettings();
        _useAzure = string.Equals(_settings.TtsProvider, "Azure", StringComparison.OrdinalIgnoreCase)
                    && AzureSpeechTtsClient.IsConfigured(_settings);
        _sapi.ApplyVoices(_settings.EnglishVoiceName, _settings.RussianVoiceName);
        _azure.ApplySettings(_settings);
        SetVolumePercent(_settings.VolumePercent);
        AppLog.Info("TTS provider: " + (_useAzure ? "Azure+cache" : "SAPI")
            + (AzureSpeechTtsClient.IsConfigured(_settings) ? " (Azure keys OK)" : " (Azure keys missing)")
            + " volume=" + _settings.VolumePercent);
    }

    public void SetVolumePercent(int percent)
    {
        var p = Math.Max(0, Math.Min(100, percent));
        _settings.VolumePercent = p;
        _sapi.SetVolumePercent(p);
        _azure.SetVolumePercent(p);
    }

    public void WarmUp()
    {
        if (_useAzure)
        {
            AppLog.Info("TTS warm-up: Azure mode, cache dir=" + TtsAudioCache.CacheDirectory);
        }
        else
        {
            _sapi.WarmUp();
        }
    }

    public void SpeakScreen(LessonScreen screen)
    {
        if (_useAzure)
        {
            _azure.SpeakScreen(screen);
        }
        else
        {
            _sapi.SpeakScreen(screen);
        }
    }

    public bool TryTogglePause() =>
        _useAzure ? _azure.TryTogglePause() : _sapi.TryTogglePause();

    public void Stop()
    {
        _azure.Stop();
        _sapi.Stop();
    }

    public Task PrefetchLessonAsync(LessonDocument lesson, IProgress<string>? progress, CancellationToken ct)
    {
        if (!_useAzure && !AzureSpeechTtsClient.IsConfigured(_settings))
        {
            throw new InvalidOperationException("Кэш озвучки доступен только для Azure. Включите Azure в настройках.");
        }

        // Prefetch always uses Azure client + selected Azure voices, even if currently on SAPI —
        // but require Azure config. Use azure player with current settings.
        _azure.ApplySettings(_settings);
        return _azure.PrefetchLessonAsync(lesson, progress, ct);
    }

    public void Dispose()
    {
        _azure.Dispose();
        _sapi.Dispose();
    }
}
