using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Claims Windows SMTC so Bluetooth Play targets EnglishLearning.
/// WinRT types are loaded only via reflection so Win7 can start without Windows.Media.
/// </summary>
public sealed class MediaFocusClaimer : IDisposable
{
    private readonly Window _window;
    private object? _player;
    private object? _smtc;
    private EventInfo? _buttonPressedEvent;
    private Delegate? _buttonPressedHandler;
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

            var statusType = _smtc.GetType().Assembly.GetType("Windows.Media.MediaPlaybackStatus");
            if (statusType == null)
            {
                return;
            }

            var name = status switch
            {
                TransportStatus.Playing => "Playing",
                TransportStatus.Paused => "Paused",
                _ => "Stopped",
            };
            var value = Enum.Parse(statusType, name);
            _smtc.GetType().GetProperty("PlaybackStatus")?.SetValue(_smtc, value);
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
        // Win7 (6.1): no WinRT SMTC. Skip before touching WinRT types.
        if (Environment.OSVersion.Version.Major < 6
            || (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor < 2))
        {
            AppLog.Info("Media focus: SMTC skipped (Windows 7 — hotkey + foreground only)");
            return;
        }

        try
        {
            var playerType = Type.GetType("Windows.Media.Playback.MediaPlayer, Windows, ContentType=WindowsRuntime")
                ?? Type.GetType("Windows.Media.Playback.MediaPlayer, Windows.Foundation.UniversalApiContract");
            if (playerType == null)
            {
                AppLog.Warn("Media focus SMTC unavailable (WinRT MediaPlayer type missing)");
                return;
            }

            _player = Activator.CreateInstance(playerType);
            if (_player == null)
            {
                return;
            }

            playerType.GetProperty("Volume")?.SetValue(_player, 0.0);
            var cmdMgr = playerType.GetProperty("CommandManager")?.GetValue(_player);
            cmdMgr?.GetType().GetProperty("IsEnabled")?.SetValue(cmdMgr, false);

            _smtc = playerType.GetProperty("SystemMediaTransportControls")?.GetValue(_player);
            if (_smtc == null)
            {
                AppLog.Warn("Media focus SMTC unavailable (null controls)");
                DisposePlayerQuiet();
                return;
            }

            var smtcType = _smtc.GetType();
            smtcType.GetProperty("IsEnabled")?.SetValue(_smtc, true);
            smtcType.GetProperty("IsPlayEnabled")?.SetValue(_smtc, true);
            smtcType.GetProperty("IsPauseEnabled")?.SetValue(_smtc, true);
            smtcType.GetProperty("IsStopEnabled")?.SetValue(_smtc, false);
            smtcType.GetProperty("IsNextEnabled")?.SetValue(_smtc, false);
            smtcType.GetProperty("IsPreviousEnabled")?.SetValue(_smtc, false);

            var updater = smtcType.GetProperty("DisplayUpdater")?.GetValue(_smtc);
            if (updater != null)
            {
                var musicType = Type.GetType("Windows.Media.MediaPlaybackType, Windows, ContentType=WindowsRuntime")
                    ?? updater.GetType().Assembly.GetType("Windows.Media.MediaPlaybackType");
                if (musicType != null)
                {
                    updater.GetType().GetProperty("Type")?.SetValue(updater, Enum.Parse(musicType, "Music"));
                }

                var music = updater.GetType().GetProperty("MusicProperties")?.GetValue(updater);
                music?.GetType().GetProperty("Title")?.SetValue(music, "Hermes English Learning");
                music?.GetType().GetProperty("Artist")?.SetValue(music, "EnglishLearning");
                updater.GetType().GetMethod("Update")?.Invoke(updater, null);
            }

            _buttonPressedEvent = smtcType.GetEvent("ButtonPressed");
            if (_buttonPressedEvent != null)
            {
                var handlerType = _buttonPressedEvent.EventHandlerType;
                if (handlerType != null)
                {
                    var method = GetType().GetMethod(nameof(OnSmtcButtonPressed), BindingFlags.Instance | BindingFlags.NonPublic);
                    _buttonPressedHandler = Delegate.CreateDelegate(handlerType, this, method!);
                    _buttonPressedEvent.AddEventHandler(_smtc, _buttonPressedHandler);
                }
            }

            AppLog.Info("Media focus: SMTC enabled (muted session for BT Play capture)");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus SMTC unavailable (" + ex.Message + ") — hotkey + foreground only");
            DisposePlayerQuiet();
        }
    }

    private void OnSmtcButtonPressed(object sender, object args)
    {
        try
        {
            var button = args.GetType().GetProperty("Button")?.GetValue(args);
            AppLog.Info("Media focus SMTC button: " + (button?.ToString() ?? "?"));
            var name = button?.ToString() ?? string.Empty;
            if (name.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ClaimForeground();
                    PlayPauseFromSystem?.Invoke();
                }));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC button handler: " + ex.Message);
        }
    }

    private void DisposePlayerQuiet()
    {
        try
        {
            (_player as IDisposable)?.Dispose();
        }
        catch
        {
        }

        _player = null;
        _smtc = null;
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
            if (_smtc != null && _buttonPressedEvent != null && _buttonPressedHandler != null)
            {
                _buttonPressedEvent.RemoveEventHandler(_smtc, _buttonPressedHandler);
            }

            _smtc?.GetType().GetProperty("IsEnabled")?.SetValue(_smtc, false);
        }
        catch
        {
        }

        DisposePlayerQuiet();
        _buttonPressedEvent = null;
        _buttonPressedHandler = null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
