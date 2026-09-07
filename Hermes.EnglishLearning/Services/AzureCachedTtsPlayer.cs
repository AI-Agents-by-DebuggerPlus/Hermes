using System;
using System.Collections.Generic;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Azure Neural TTS with local WAV cache.
/// Win7: System.Media.SoundPlayer (WPF MediaPlayer+MP3 often native-crashes).
/// Win8+: WPF MediaPlayer.
/// </summary>
public sealed class AzureCachedTtsPlayer : IDisposable
{
    private readonly AzureSpeechTtsClient _client = new();
    private readonly object _sync = new();
    private readonly bool _useSoundPlayer = OsInfo.IsWindows7OrOlder;
    private MediaPlayer? _player;
    private SoundPlayer? _sound;
    private AppSettings _settings = new();
    private readonly Queue<string> _queue = new();
    private bool _paused;
    private bool _active;
    private bool _preparing;
    private bool _endedWhilePaused;
    private bool _disposed;
    private CancellationTokenSource? _speakCts;
    private CancellationTokenSource? _clipCts;
    private double _volume = 0.8;
    private string? _currentPath;
    private DispatcherTimer? _watchdog;
    private TimeSpan _lastWatchPos;
    private int _stuckTicks;

    public event EventHandler? SpeakCompleted;

    public AzureCachedTtsPlayer()
    {
        AppLog.Info("Azure TTS player mode: " + (_useSoundPlayer ? "SoundPlayer (Win7-safe WAV)" : "MediaPlayer")
            + " · OS " + OsInfo.Version);

        if (_useSoundPlayer)
        {
            _sound = new SoundPlayer();
            return;
        }

        _player = new MediaPlayer();
        _player.MediaEnded += (_, __) => Application.Current?.Dispatcher.BeginInvoke(new Action(OnMediaEnded));
        _player.MediaFailed += (_, e) =>
        {
            AppLog.Error("MediaPlayer failed: " + (e.ErrorException?.Message ?? "unknown"));
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_sync)
                {
                    _endedWhilePaused = false;
                }

                PlayNextOrComplete();
            }));
        };

        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdog.Tick += (_, __) => WatchdogTick();
        _watchdog.Start();
    }

    private void OnMediaEnded()
    {
        lock (_sync)
        {
            if (_paused)
            {
                _endedWhilePaused = true;
                AppLog.Info("TTS clip ended while paused — will advance on resume");
                return;
            }
        }

        PlayNextOrComplete();
    }

    private void WatchdogTick()
    {
        if (_disposed || _player == null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_active || _paused || _preparing)
            {
                _stuckTicks = 0;
                return;
            }
        }

        try
        {
            var pos = _player.Position;
            if (pos <= _lastWatchPos + TimeSpan.FromMilliseconds(50))
            {
                _stuckTicks++;
            }
            else
            {
                _stuckTicks = 0;
            }

            _lastWatchPos = pos;

            var atEnd = false;
            try
            {
                atEnd = _player.NaturalDuration.HasTimeSpan
                        && pos >= _player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(250);
            }
            catch
            {
            }

            if (_endedWhilePaused || atEnd || _stuckTicks >= 2)
            {
                AppLog.Warn("TTS watchdog recover: stuckTicks=" + _stuckTicks
                    + " atEnd=" + atEnd + " endedWhilePaused=" + _endedWhilePaused);
                _endedWhilePaused = false;
                _stuckTicks = 0;
                PlayNextOrComplete();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("TTS watchdog: " + ex.Message);
        }
    }

    public bool IsSpeaking
    {
        get
        {
            lock (_sync)
            {
                return _active && !_paused;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _active && _paused;
            }
        }
    }

    public bool HasActiveOrPausedUtterance
    {
        get
        {
            lock (_sync)
            {
                return _active || _preparing;
            }
        }
    }

    public void ApplySettings(AppSettings settings) => _settings = settings ?? new AppSettings();

    public void SetVolumePercent(int percent)
    {
        var p = Math.Max(0, Math.Min(100, percent));
        _volume = p / 100.0;
        if (_player != null)
        {
            _player.Volume = _volume;
        }
    }

    public async void SpeakScreen(LessonScreen screen)
    {
        try
        {
            await SpeakScreenAsync(screen, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Azure speak screen failed", ex);
            RaiseCompleted();
        }
    }

    public async Task SpeakScreenAsync(LessonScreen screen, CancellationToken ct)
    {
        StopInternal(cancelSpeak: true);
        if (!AzureSpeechTtsClient.IsConfigured(_settings))
        {
            AppLog.Warn("Azure TTS not configured (key/endpoint)");
            RaiseCompleted();
            return;
        }

        var units = TtsUtterancePlanner.FromScreen(screen);
        if (units.Count == 0)
        {
            RaiseCompleted();
            return;
        }

        lock (_sync)
        {
            _preparing = true;
            _paused = false;
        }

        _speakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _speakCts.Token;
        var paths = new List<string>();
        try
        {
            foreach (var u in units)
            {
                token.ThrowIfCancellationRequested();
                var path = await EnsureCachedAsync(u, playContext: true, token).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _preparing = false;
                _active = false;
            }

            return;
        }

        if (paths.Count == 0)
        {
            lock (_sync)
            {
                _preparing = false;
            }

            RaiseCompleted();
            return;
        }

        lock (_sync)
        {
            _queue.Clear();
            foreach (var p in paths)
            {
                _queue.Enqueue(p);
            }

            _preparing = false;
            _active = true;
            _paused = false;
        }

        AppLog.Info("Azure TTS playlist ready: " + paths.Count + " WAV clips");
        PlayNextOrComplete();
    }

    public async Task PrefetchLessonAsync(
        LessonDocument lesson,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!AzureSpeechTtsClient.IsConfigured(_settings))
        {
            throw new InvalidOperationException("Azure не настроен: нужен ключ и endpoint в settings.json");
        }

        var units = TtsUtterancePlanner.FromLesson(lesson);
        var total = units.Count;
        var done = 0;
        var hits = 0;
        var writes = 0;
        foreach (var u in units)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var voice = u.English ? _settings.AzureEnglishVoice : _settings.AzureRussianVoice;
            var locale = u.English ? "en-US" : "ru-RU";
            progress?.Report("Кэш " + done + "/" + total + ": " + Truncate(u.Text, 40));

            if (TtsAudioCache.TryGetExisting(voice, locale, u.Text, out _))
            {
                hits++;
                AppLog.Info("TTS cache already present (prefetch skip): voice=" + voice + " text=" + Truncate(u.Text, 60));
                continue;
            }

            await EnsureCachedAsync(u, playContext: false, ct).ConfigureAwait(false);
            writes++;
        }

        AppLog.Info("TTS prefetch done: total=" + total + " alreadyCached=" + hits + " downloaded=" + writes);
        progress?.Report("Готово: в кэше " + total + " (скачано " + writes + ", уже было " + hits + ")");
    }

    public bool TryTogglePause()
    {
        lock (_sync)
        {
            if (_preparing)
            {
                AppLog.Info("Azure TTS preparing playlist — Play ignored (wait for audio)");
                return true;
            }

            if (!_active)
            {
                return false;
            }

            if (_paused)
            {
                _paused = false;
                if (_useSoundPlayer)
                {
                    // SoundPlayer cannot resume mid-clip — replay current or advance.
                    if (!string.IsNullOrEmpty(_currentPath))
                    {
                        var path = _currentPath;
                        AppLog.Info("Azure TTS resume (SoundPlayer): replay current clip");
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => StartSoundClip(path!)));
                        return true;
                    }

                    Application.Current?.Dispatcher.BeginInvoke(new Action(PlayNextOrComplete));
                    return true;
                }

                if (_endedWhilePaused || IsCurrentClipFinishedUnlocked())
                {
                    _endedWhilePaused = false;
                    AppLog.Info("Azure TTS resume → advance to next clip (previous ended while paused)");
                    Application.Current?.Dispatcher.BeginInvoke(new Action(PlayNextOrComplete));
                    return true;
                }

                try
                {
                    if (_player != null)
                    {
                        _player.Volume = _volume;
                        _player.Play();
                        AppLog.Info("Azure TTS resumed pos=" + _player.Position.TotalSeconds.ToString("0.0") + "s");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Azure TTS resume failed, advancing: " + ex.Message);
                    Application.Current?.Dispatcher.BeginInvoke(new Action(PlayNextOrComplete));
                }

                return true;
            }

            try
            {
                if (_useSoundPlayer)
                {
                    CancelClip();
                    try
                    {
                        _sound?.Stop();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    _player?.Pause();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Azure TTS pause: " + ex.Message);
            }

            _paused = true;
            AppLog.Info("Azure TTS paused");
            return true;
        }
    }

    private bool IsCurrentClipFinishedUnlocked()
    {
        try
        {
            if (_player == null || !_player.NaturalDuration.HasTimeSpan)
            {
                return false;
            }

            return _player.Position >= _player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(200);
        }
        catch
        {
            return false;
        }
    }

    public void Stop() => StopInternal(cancelSpeak: true);

    private async Task<string> EnsureCachedAsync(TtsUtterance u, bool playContext, CancellationToken ct)
    {
        var voice = u.English
            ? (string.IsNullOrWhiteSpace(_settings.AzureEnglishVoice) ? "en-US-JennyNeural" : _settings.AzureEnglishVoice)
            : (string.IsNullOrWhiteSpace(_settings.AzureRussianVoice) ? "ru-RU-SvetlanaNeural" : _settings.AzureRussianVoice);
        var locale = u.English ? "en-US" : "ru-RU";

        if (TtsAudioCache.TryGetExisting(voice, locale, u.Text, out var path))
        {
            if (playContext)
            {
                AppLog.Info("TTS cache HIT — play local file (no Azure request): " + System.IO.Path.GetFileName(path)
                    + " | " + Truncate(u.Text, 50));
            }

            return path;
        }

        AppLog.Info("TTS cache MISS — Azure synthesize WAV: voice=" + voice + " | " + Truncate(u.Text, 50));
        var audio = await _client.SynthesizeAsync(_settings, u.Text, voice, locale, ct).ConfigureAwait(false);
        path = TtsAudioCache.GetPath(voice, locale, u.Text);
        TtsAudioCache.Save(path, audio);
        AppLog.Info("TTS cache WRITE: " + System.IO.Path.GetFileName(path) + " bytes=" + audio.Length);
        return path;
    }

    private void PlayNextOrComplete()
    {
        if (_disposed)
        {
            return;
        }

        string? next = null;
        lock (_sync)
        {
            if (_paused)
            {
                return;
            }

            if (_queue.Count > 0)
            {
                next = _queue.Dequeue();
            }
            else
            {
                _active = false;
            }
        }

        if (next == null)
        {
            AppLog.Info("Azure TTS playlist finished");
            RaiseCompleted();
            return;
        }

        if (_useSoundPlayer)
        {
            StartSoundClip(next);
            return;
        }

        try
        {
            AppLog.Info("TTS play MediaPlayer: " + System.IO.Path.GetFileName(next));
            _currentPath = next;
            _endedWhilePaused = false;
            _stuckTicks = 0;
            _lastWatchPos = TimeSpan.Zero;
            if (_player != null)
            {
                _player.Volume = _volume;
                _player.Open(new Uri(next, UriKind.Absolute));
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to play cached clip", ex);
            PlayNextOrComplete();
        }
    }

    private void StartSoundClip(string path)
    {
        CancelClip();
        _clipCts = new CancellationTokenSource();
        var token = _clipCts.Token;
        _currentPath = path;
        AppLog.Info("TTS play SoundPlayer: " + System.IO.Path.GetFileName(path));

        Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using var player = new SoundPlayer(path);
                player.Load();
                token.ThrowIfCancellationRequested();
                player.PlaySync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.Error("SoundPlayer failed: " + ex.Message);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_sync)
                {
                    if (_paused || _disposed)
                    {
                        return;
                    }
                }

                PlayNextOrComplete();
            }));
        }, token);
    }

    private void CancelClip()
    {
        try
        {
            _clipCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _clipCts?.Dispose();
        }
        catch
        {
        }

        _clipCts = null;
        try
        {
            _sound?.Stop();
        }
        catch
        {
        }
    }

    private void StopInternal(bool cancelSpeak)
    {
        if (cancelSpeak)
        {
            try
            {
                _speakCts?.Cancel();
            }
            catch
            {
            }

            _speakCts?.Dispose();
            _speakCts = null;
        }

        CancelClip();

        lock (_sync)
        {
            _queue.Clear();
            _active = false;
            _preparing = false;
            _paused = false;
            _endedWhilePaused = false;
            _currentPath = null;
        }

        try
        {
            _player?.Stop();
            _player?.Close();
        }
        catch
        {
        }
    }

    private void RaiseCompleted()
    {
        lock (_sync)
        {
            _active = false;
            _preparing = false;
            _paused = false;
        }

        SpeakCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchdog?.Stop();
        StopInternal(cancelSpeak: true);
        try
        {
            _player?.Close();
        }
        catch
        {
        }

        try
        {
            _sound?.Dispose();
        }
        catch
        {
        }
    }
}
