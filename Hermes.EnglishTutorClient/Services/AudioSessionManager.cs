using System;
using System.Windows;
using System.Windows.Threading;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Single owner of SMTC + Bluetooth audio claim + media Play routing.
/// Prevents duplicate MediaFocusClaimer instances and reclaim during listen / Play-test.
/// </summary>
public sealed class AudioSessionManager : IDisposable
{
    public enum SessionMode
    {
        Idle,
        Listening,
        PlayTest,
        MicTest,
    }

    private readonly Window _window;
    private MediaFocusClaimer? _media;
    private HeadsetAudioClaimer? _audio;
    private MediaPlayHotkey? _hotkey;
    private DispatcherTimer? _smtcKeepAlive;
    private SessionMode _mode = SessionMode.Idle;
    private bool _disposed;
    private DateTime _lastPlayUtc = DateTime.MinValue;

    public event Action? PlayPausePressed;
    public event Action? PlayTestPressed;

    public SessionMode Mode => _mode;
    public bool BlocksReclaim =>
        _mode == SessionMode.Listening
        || _mode == SessionMode.PlayTest
        || _mode == SessionMode.MicTest;

    public AudioSessionManager(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void Start()
    {
        if (_disposed) return;
        _hotkey = new MediaPlayHotkey(_window, tag: "AudioSession");
        _hotkey.PlayPausePressed += () => DispatchPlay("hotkey");
        _media = new MediaFocusClaimer(_window);
        _media.PlayPauseFromSystem += () => DispatchPlay("SMTC");
        _media.Start();

        _audio = new HeadsetAudioClaimer();
        _audio.AfterHoldResumed = () =>
        {
            try { _media?.ReassertPlayingStatus(); }
            catch (Exception ex) { AppLog.Warn("AudioSession AfterHoldResumed: " + ex.Message); }
        };
        _audio.Claim();

        _smtcKeepAlive = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _smtcKeepAlive.Tick += (_, __) =>
        {
            if (_mode != SessionMode.Listening && _mode != SessionMode.PlayTest) return;
            try { _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing); }
            catch { /* ignore */ }
        };

        _window.Activated += OnWindowActivated;
        AppLog.Info("AudioSessionManager started");
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_disposed || BlocksReclaim && _mode == SessionMode.MicTest) return;
        try
        {
            _media?.ClaimForeground();
            if (_mode != SessionMode.MicTest)
                _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
        }
        catch { /* ignore */ }
    }

    private void DispatchPlay(string source)
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        if ((now - _lastPlayUtc).TotalMilliseconds < 120)
        {
            AppLog.Info("AudioSession Play debounced (" + source + ")");
            return;
        }

        _lastPlayUtc = now;
        AppLog.Info("AudioSession Play source=" + source + " mode=" + _mode);

        try
        {
            if (_mode == SessionMode.PlayTest)
                PlayTestPressed?.Invoke();
            else if (_mode != SessionMode.MicTest)
                PlayPausePressed?.Invoke();
            else
                AppLog.Info("AudioSession Play ignored during MicTest");
        }
        catch (Exception ex)
        {
            AppLog.Warn("AudioSession Play handler: " + ex.Message);
        }
    }

    public void EnterListening()
    {
        if (_disposed) return;
        _mode = SessionMode.Listening;
        _media?.PauseSilentPlayerForMic();
        _audio?.PauseHoldForCapture();
        _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
        try { _smtcKeepAlive?.Start(); } catch { /* ignore */ }
        AppLog.Info("AudioSession mode=Listening");
    }

    public void ExitListening(bool rebuild = true)
    {
        if (_disposed) return;
        try { _smtcKeepAlive?.Stop(); } catch { /* ignore */ }
        _media?.ResumeSilentPlayerAfterMic();
        _audio?.ResumeHoldAfterCapture();
        if (rebuild)
        {
            try { _media?.RebuildSession(); }
            catch (Exception ex) { AppLog.Warn("AudioSession ExitListening rebuild: " + ex.Message); }
        }

        _mode = SessionMode.Idle;
        AppLog.Info("AudioSession mode=Idle (after Listening)");
    }

    public void BeginPlayTest()
    {
        if (_disposed) return;
        _mode = SessionMode.PlayTest;
        _media?.SetSmtcEnabled(true);
        _media?.ClaimForeground();
        try { _media?.RebuildSession(); } catch { /* ignore */ }
        _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
        try { _smtcKeepAlive?.Start(); } catch { /* ignore */ }
        AppLog.Info("AudioSession mode=PlayTest (single SMTC owner)");
    }

    public void EndPlayTest()
    {
        if (_disposed) return;
        try { _smtcKeepAlive?.Stop(); } catch { /* ignore */ }
        _mode = SessionMode.Idle;
        try { _media?.RebuildSession(); } catch { /* ignore */ }
        _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
        AppLog.Info("AudioSession mode=Idle (after PlayTest)");
    }

    public void BeginMicTest()
    {
        if (_disposed) return;
        _mode = SessionMode.MicTest;
        _media?.PauseSilentPlayerForMic();
        _audio?.PauseHoldForCapture();
        AppLog.Info("AudioSession mode=MicTest");
    }

    public void EndMicTest()
    {
        if (_disposed) return;
        _media?.ResumeSilentPlayerAfterMic();
        _audio?.ResumeHoldAfterCapture();
        try { _media?.RebuildSession(); } catch { /* ignore */ }
        _mode = SessionMode.Idle;
        AppLog.Info("AudioSession mode=Idle (after MicTest)");
    }

    /// <summary>Headset reconnect reclaim — skipped while listening / play-test / mic-test.</summary>
    public bool TryReclaimOnHeadsetAppear(string headsetName)
    {
        if (_disposed) return false;
        if (BlocksReclaim)
        {
            AppLog.Info("AudioSession skip Reclaim (mode=" + _mode + ") headset=" + headsetName);
            return false;
        }

        try
        {
            _audio?.Reclaim();
            _media?.ClaimForeground();
            _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
            AppLog.Info("AudioSession reclaimed for " + headsetName);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("AudioSession Reclaim: " + ex.Message);
            return false;
        }
    }

    public void ClaimForeground() => _media?.ClaimForeground();

    public void SetPlaying() =>
        _media?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _window.Activated -= OnWindowActivated; } catch { /* ignore */ }
        try { _smtcKeepAlive?.Stop(); } catch { /* ignore */ }
        _smtcKeepAlive = null;
        try { _hotkey?.Dispose(); } catch { /* ignore */ }
        _hotkey = null;
        try { _media?.Dispose(); } catch { /* ignore */ }
        _media = null;
        try { _audio?.Dispose(); } catch { /* ignore */ }
        _audio = null;
        AppLog.Info("AudioSessionManager disposed");
    }
}
