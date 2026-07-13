using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Media;
using Windows.Media.Playback;
using WinRTMediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Claims Windows SMTC so Bluetooth Play targets EnglishLearning.
/// Uses a muted WinRT MediaPlayer (may appear as a second mixer row at 0%).
/// </summary>
public sealed class MediaFocusClaimer : IDisposable
{
    private readonly Window _window;
    private WinRTMediaPlayer? _player;
    private SystemMediaTransportControls? _smtc;
    private bool _disposed;

    public event Action? PlayPauseFromSystem;

    public MediaFocusClaimer(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public enum TransportStatus
    {
        Stopped,
        Playing,
        Paused,
    }

    public void Start()
    {
        ClaimForeground();
        TryEnableSmtc();
        SetTransportStatus(TransportStatus.Stopped);
        AppLog.Info("Media focus claimer started");
    }

    public void SetTransportStatus(TransportStatus status)
    {
        try
        {
            if (_smtc == null)
            {
                return;
            }

            _smtc.PlaybackStatus = status switch
            {
                TransportStatus.Playing => MediaPlaybackStatus.Playing,
                TransportStatus.Paused => MediaPlaybackStatus.Paused,
                _ => MediaPlaybackStatus.Stopped,
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC status: " + ex.Message);
        }
    }

    public void ClaimForeground()
    {
        try
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus ClaimForeground: " + ex.Message);
        }
    }

    private void TryEnableSmtc()
    {
        try
        {
            _player = new WinRTMediaPlayer { Volume = 0 };
            _player.CommandManager.IsEnabled = false;
            _smtc = _player.SystemMediaTransportControls;
            _smtc.IsEnabled = true;
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsStopEnabled = false;
            _smtc.IsNextEnabled = false;
            _smtc.IsPreviousEnabled = false;

            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = "Hermes English Learning";
            updater.MusicProperties.Artist = "EnglishLearning";
            updater.Update();

            _smtc.ButtonPressed += OnSmtcButtonPressed;
            AppLog.Info("Media focus: SMTC enabled (muted session for BT Play capture)");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus SMTC unavailable (" + ex.Message + ") — hotkey + foreground only");
            _player = null;
            _smtc = null;
        }
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        AppLog.Info("Media focus SMTC button: " + args.Button);
        if (args.Button == SystemMediaTransportControlsButton.Play
            || args.Button == SystemMediaTransportControlsButton.Pause)
        {
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                ClaimForeground();
                PlayPauseFromSystem?.Invoke();
            }));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_smtc != null)
            {
                _smtc.ButtonPressed -= OnSmtcButtonPressed;
                _smtc.IsEnabled = false;
            }
        }
        catch
        {
        }

        try
        {
            _player?.Dispose();
        }
        catch
        {
        }

        _smtc = null;
        _player = null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
