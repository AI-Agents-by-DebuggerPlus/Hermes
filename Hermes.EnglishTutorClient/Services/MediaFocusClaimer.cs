using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Claims Windows SMTC so Bluetooth AVRCP Play targets EnglishTutorClient (not Chrome/Gemini).
/// WinRT types loaded via reflection so Win7 can start without Windows.Media.
/// </summary>
public sealed class MediaFocusClaimer : IDisposable
{
    private readonly Window _window;
    private object? _player;
    private object? _smtc;
    private MethodInfo? _removeButtonPressed;
    private object? _buttonPressedToken;
    private Delegate? _buttonPressedHandler;
    private string? _silentWavPath;
    private bool _disposed;
    private bool _silentPausedForMic;

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
        AppLog.Info("MediaFocusClaimer start");
        LogPackageIdentityOnce();
        ClaimForeground();
        TryEnableSmtc();
        SetTransportStatus(TransportStatus.Playing);
        AppLog.Info("MediaFocusClaimer smtc=" + (_smtc != null ? "ok" : "null"));
        _ = LogCompetingSessionsAsync();
    }

    private static int _loggedPackageIdentity;
    private static int _competingSessionsLogged;

    /// <summary>One-time D2 check: unpackaged Win32 may lack WinRT media session manager.</summary>
    public static void LogPackageIdentityOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _loggedPackageIdentity, 1) != 0) return;
        try
        {
            var pkgType = Type.GetType(
                "Windows.ApplicationModel.Package, Windows, ContentType=WindowsRuntime");
            var currentProp = pkgType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            var pkg = currentProp?.GetValue(null);
            if (pkg == null)
            {
                AppLog.Warn("Unpackaged app (no Package.Current) — session-manager diag limited (D2)");
                return;
            }

            var id = pkg.GetType().GetProperty("Id")?.GetValue(pkg);
            var fullName = id?.GetType().GetProperty("FullName")?.GetValue(id)?.ToString();
            AppLog.Info("Package identity: " + (fullName ?? "(unknown)"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Unpackaged app — " + ex.GetType().Name + " (D2)");
        }
    }

    /// <summary>
    /// Keep SMTC alive while mic uses HFP. Do NOT pause MediaPlayer — pausing drops AVRCP
    /// and the second headset Play never arrives (start works, stop/send does not).
    /// A2DP release is HeadsetAudioClaimer.PauseHoldForCapture only.
    /// </summary>
    public void PauseSilentPlayerForMic()
    {
        if (_disposed) return;
        _silentPausedForMic = true;
        try
        {
            if (_smtc != null)
            {
                _smtc.GetType().GetProperty("IsEnabled")?.SetValue(_smtc, true);
                _smtc.GetType().GetProperty("IsPlayEnabled")?.SetValue(_smtc, true);
                _smtc.GetType().GetProperty("IsPauseEnabled")?.SetValue(_smtc, true);
            }

            // Keep silent loop running so the session stays the active media target.
            if (_player != null)
            {
                try { _player.GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(_player, null); }
                catch { /* ignore */ }
            }

            SetTransportStatus(TransportStatus.Playing);
            AppLog.Info("MediaFocusClaimer: SMTC kept Playing during mic (MediaPlayer not paused)");
        }
        catch (Exception ex)
        {
            AppLog.Warn("MediaFocusClaimer.PauseSilentPlayerForMic: " + ex.Message);
        }
    }

    /// <summary>Resume muted MediaPlayer after mic so BT Play keeps targeting Tutor.</summary>
    public void ResumeSilentPlayerAfterMic()
    {
        if (_disposed) return;
        _silentPausedForMic = false;
        try
        {
            ClaimForeground();
            SetTransportStatus(TransportStatus.Playing);
            if (_player != null)
            {
                var playerType = _player.GetType();
                TryPlaySilentLoop(playerType);
            }
            AppLog.Info("MediaFocusClaimer: silent MediaPlayer resumed after mic");
        }
        catch (Exception ex)
        {
            AppLog.Warn("MediaFocusClaimer.ResumeSilentPlayerAfterMic: " + ex.Message);
        }
    }

    /// <summary>
    /// Tear down and recreate MediaPlayer/SMTC after HFP↔A2DP switch (second Play often dies otherwise).
    /// </summary>
    public void RebuildSession()
    {
        if (_disposed) return;
        try
        {
            AppLog.Info("MediaFocusClaimer: rebuilding SMTC session…");
            TryUnsubscribeButtonPressed();
            try { _player?.GetType().GetMethod("Pause", Type.EmptyTypes)?.Invoke(_player, null); }
            catch { /* ignore */ }
            DisposePlayerQuiet();
            _silentPausedForMic = false;
            _buttonPressedHandler = null;
            _buttonPressedToken = null;
            _removeButtonPressed = null;
            ClaimForeground();
            TryEnableSmtc();
            SetTransportStatus(TransportStatus.Playing);
            AppLog.Info("MediaFocusClaimer: rebuild done smtc=" + (_smtc != null ? "ok" : "null"));
        }
        catch (Exception ex)
        {
            AppLog.Error("MediaFocusClaimer.RebuildSession failed: " + ex.Message);
        }
    }

    /// <summary>
    /// After audio endpoint reclaim (A2DP hold resume), re-assert Playing and ButtonPressed.
    /// </summary>
    public void ReassertPlayingStatus()
    {
        if (_smtc == null)
        {
            AppLog.Warn("MediaFocusClaimer.ReassertPlayingStatus: SMTC null — rebuilding");
            RebuildSession();
            return;
        }

        try
        {
            ClaimForeground();
            _smtc.GetType().GetProperty("IsEnabled")?.SetValue(_smtc, true);
            _smtc.GetType().GetProperty("IsPlayEnabled")?.SetValue(_smtc, true);
            _smtc.GetType().GetProperty("IsPauseEnabled")?.SetValue(_smtc, true);
            SetTransportStatus(TransportStatus.Playing);
            // Defensive re-subscribe in case OS dropped the handler during endpoint change
            TryUnsubscribeButtonPressed();
            if (_smtc != null && !TrySubscribeButtonPressed(_smtc.GetType()))
                AppLog.Warn("MediaFocusClaimer: ButtonPressed re-subscribe failed");

            if (!_silentPausedForMic)
            {
                try { _player?.GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(_player, null); }
                catch { /* ignore */ }
            }

            AppLog.Info("MediaFocusClaimer: ReassertPlayingStatus ok");
        }
        catch (Exception ex)
        {
            AppLog.Error("MediaFocusClaimer.ReassertPlayingStatus failed: " + ex.Message);
            RebuildSession();
        }
    }

    /// <summary>Diagnostic: which app currently owns system media session (P1 check).</summary>
    public static async System.Threading.Tasks.Task LogCompetingSessionsAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _competingSessionsLogged, 1) != 0)
            return;

        var apt = System.Threading.Thread.CurrentThread.GetApartmentState();
        AppLog.Info("SMTC sessions diag: apartment=" + apt);

        try
        {
            // Avoid Task.Run (MTA) — WinRT media APIs often need STA (D1).
            var mgrType = Type.GetType(
                "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows, ContentType=WindowsRuntime");
            if (mgrType == null)
            {
                AppLog.Info("Competing sessions check: GlobalSystemMediaTransportControlsSessionManager type missing");
                return;
            }

            var request = mgrType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static);
            if (request == null)
            {
                AppLog.Info("Competing sessions check: RequestAsync missing");
                return;
            }

            object? op;
            try
            {
                op = request.Invoke(null, null);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                AppLog.Error("LogCompetingSessionsAsync: RequestAsync threw: " + inner.GetType().Name
                    + ": " + inner.Message + Environment.NewLine + inner.StackTrace);
                return;
            }

            if (op == null)
            {
                AppLog.Error("LogCompetingSessionsAsync: RequestAsync() returned null operation WITHOUT throwing — "
                    + "possible packaging/apartment-state issue (D1/D2)");
                return;
            }

            object? manager = null;
            try
            {
                var asTask = op.GetType().GetMethod("AsTask", Type.EmptyTypes)
                             ?? op.GetType().GetMethods().FirstOrDefault(m =>
                                 m.Name == "AsTask" && m.GetParameters().Length == 0);
                if (asTask != null)
                {
                    var task = asTask.Invoke(op, null) as System.Threading.Tasks.Task;
                    if (task != null)
                    {
                        var completed = await System.Threading.Tasks.Task.WhenAny(
                            task, System.Threading.Tasks.Task.Delay(3000)).ConfigureAwait(false);
                        if (completed != task)
                        {
                            AppLog.Error("LogCompetingSessionsAsync: RequestAsync AsTask timed out (3s)");
                            return;
                        }

                        if (task.IsFaulted)
                        {
                            var ex = task.Exception?.GetBaseException() ?? task.Exception;
                            AppLog.Error("LogCompetingSessionsAsync: RequestAsync task faulted: "
                                + ex?.GetType().Name + ": " + ex?.Message + Environment.NewLine + ex?.StackTrace);
                            return;
                        }

                        var resultProp = task.GetType().GetProperty("Result");
                        manager = resultProp?.GetValue(task);
                    }
                }
                else
                {
                    var getResults = op.GetType().GetMethod("GetResults");
                    for (var i = 0; i < 30; i++)
                    {
                        var status = op.GetType().GetProperty("Status")?.GetValue(op)?.ToString();
                        if (status == "Completed" || status == "Error" || status == "Canceled")
                            break;
                        await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    try
                    {
                        manager = getResults?.Invoke(op, null);
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException tie && tie.InnerException != null
                            ? tie.InnerException : ex;
                        AppLog.Error("LogCompetingSessionsAsync: GetResults threw: " + inner.GetType().Name
                            + ": " + inner.Message + Environment.NewLine + inner.StackTrace);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("LogCompetingSessionsAsync: awaiting RequestAsync failed: " + ex.GetType().Name
                    + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
                return;
            }

            if (manager == null)
            {
                AppLog.Error("SMTC sessions: RequestAsync returned null (D2 unpackaged)");
                return;
            }

            var current = manager.GetType().GetMethod("GetCurrentSession")?.Invoke(manager, null);
            var aumid = current?.GetType().GetProperty("SourceAppUserModelId")?.GetValue(current)?.ToString();
            AppLog.Info("Competing sessions check: current system session = " + (aumid ?? "(none)"));

            var getSessions = manager.GetType().GetMethod("GetSessions");
            var sessions = getSessions?.Invoke(manager, null) as System.Collections.IEnumerable;
            if (sessions != null)
            {
                var i = 0;
                foreach (var s in sessions)
                {
                    var id = s.GetType().GetProperty("SourceAppUserModelId")?.GetValue(s)?.ToString() ?? "?";
                    AppLog.Info("Competing sessions[" + i + "]=" + id);
                    i++;
                    if (i >= 8) break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("LogCompetingSessionsAsync failed: " + ex.GetType().Name + ": " + ex.Message
                + Environment.NewLine + ex.StackTrace);
        }
    }

    public void SetSmtcEnabled(bool enabled)
    {
        if (_disposed || _smtc == null) return;
        try
        {
            _smtc.GetType().GetProperty("IsEnabled")?.SetValue(_smtc, enabled);
            if (enabled)
                SetTransportStatus(TransportStatus.Playing);
            else
                SetTransportStatus(TransportStatus.Stopped);
            AppLog.Info("MediaFocusClaimer: IsEnabled=" + enabled);
        }
        catch (Exception ex)
        {
            AppLog.Warn("MediaFocusClaimer.SetSmtcEnabled: " + ex.Message);
        }
    }

    public void SetTransportStatus(TransportStatus status)
    {
        try
        {
            if (_smtc == null) return;

            var statusType = _smtc.GetType().Assembly.GetType("Windows.Media.MediaPlaybackStatus")
                             ?? Type.GetType("Windows.Media.MediaPlaybackStatus, Windows, ContentType=WindowsRuntime");
            if (statusType == null) return;

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
            if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;

            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus ClaimForeground: " + ex.Message);
        }
    }

    private void TryEnableSmtc()
    {
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
            if (_player == null) return;

            playerType.GetProperty("Volume")?.SetValue(_player, 0.0);
            playerType.GetProperty("IsLoopingEnabled")?.SetValue(_player, true);

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
                    updater.GetType().GetProperty("Type")?.SetValue(updater, Enum.Parse(musicType, "Music"));

                var music = updater.GetType().GetProperty("MusicProperties")?.GetValue(updater);
                music?.GetType().GetProperty("Title")?.SetValue(music, "Hermes English Tutor");
                music?.GetType().GetProperty("Artist")?.SetValue(music, "EnglishTutorClient");
                updater.GetType().GetMethod("Update")?.Invoke(updater, null);
            }

            // EventInfo.AddEventHandler fails on WinRT ("dynamically is not supported").
            // Call add_ButtonPressed directly instead.
            if (!TrySubscribeButtonPressed(smtcType))
                AppLog.Warn("SMTC ButtonPressed subscribe failed — using hotkey/LL only, session still claimed");

            TryPlaySilentLoop(playerType);
            AppLog.Info("MediaFocusClaimer SMTC ok");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus SMTC unavailable (" + ex.Message + ") — hotkey + foreground only");
            if (_smtc == null)
                DisposePlayerQuiet();
        }
    }

    private void TryUnsubscribeButtonPressed()
    {
        try
        {
            if (_smtc == null || _removeButtonPressed == null || _buttonPressedHandler == null)
                return;
            if (_buttonPressedToken != null)
                _removeButtonPressed.Invoke(_smtc, new[] { _buttonPressedToken });
            else
                _removeButtonPressed.Invoke(_smtc, new object[] { _buttonPressedHandler });
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC remove_ButtonPressed: " + ex.Message);
        }
        finally
        {
            _buttonPressedToken = null;
            // keep handler instance for re-add
        }
    }

    private bool TrySubscribeButtonPressed(Type smtcType)
    {
        try
        {
            var add = smtcType.GetMethod("add_ButtonPressed");
            _removeButtonPressed = smtcType.GetMethod("remove_ButtonPressed");
            if (add == null) return false;

            var handlerType = add.GetParameters()[0].ParameterType;
            var method = GetType().GetMethod(nameof(OnSmtcButtonPressed), BindingFlags.Instance | BindingFlags.NonPublic);
            _buttonPressedHandler = Delegate.CreateDelegate(handlerType, this, method!);
            _buttonPressedToken = add.Invoke(_smtc, new object[] { _buttonPressedHandler });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("SMTC add_ButtonPressed: " + ex.Message);
            _buttonPressedHandler = null;
            _buttonPressedToken = null;
            return false;
        }
    }

    private void TryPlaySilentLoop(Type playerType)
    {
        try
        {
            _silentWavPath = EnsureSilentWav();
            var uri = new Uri(_silentWavPath);
            var setUri = playerType.GetMethod("SetUriSource", new[] { typeof(Uri) });
            if (setUri != null)
            {
                setUri.Invoke(_player, new object[] { uri });
            }
            else
            {
                // MediaSource path
                var mediaSourceType = Type.GetType("Windows.Media.Core.MediaSource, Windows, ContentType=WindowsRuntime");
                var create = mediaSourceType?.GetMethod("CreateFromUri", BindingFlags.Public | BindingFlags.Static);
                var source = create?.Invoke(null, new object[] { uri });
                if (source != null)
                    playerType.GetProperty("Source")?.SetValue(_player, source);
            }

            playerType.GetMethod("Play", Type.EmptyTypes)?.Invoke(_player, null);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Media focus silent play: " + ex.Message);
        }
    }

    private static string EnsureSilentWav()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hermes.EnglishTutorClient");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "silent_1s.wav");
        if (File.Exists(path) && new FileInfo(path).Length > 40)
            return path;

        // Minimal 1s mono 8kHz 16-bit PCM silence WAV
        const int sampleRate = 8000;
        const int seconds = 1;
        var dataSize = sampleRate * seconds * 2;
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            bw.Write(new byte[dataSize]);
        }

        return path;
    }

    private void OnSmtcButtonPressed(object sender, object args)
    {
        try
        {
            var button = args.GetType().GetProperty("Button")?.GetValue(args);
            // Log EVERY SMTC button, even if filtered later (diagnostics for dead Play).
            AppLog.Info("MediaFocusClaimer: SMTC button pressed: " + (button?.ToString() ?? "?"));
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
        try { (_player as IDisposable)?.Dispose(); } catch { /* ignore */ }
        _player = null;
        _smtc = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_smtc != null && _removeButtonPressed != null && _buttonPressedHandler != null)
            {
                try
                {
                    if (_buttonPressedToken != null)
                        _removeButtonPressed.Invoke(_smtc, new[] { _buttonPressedToken });
                    else
                        _removeButtonPressed.Invoke(_smtc, new object[] { _buttonPressedHandler });
                }
                catch { /* ignore */ }
            }

            _smtc?.GetType().GetProperty("IsEnabled")?.SetValue(_smtc, false);
            _player?.GetType().GetMethod("Pause", Type.EmptyTypes)?.Invoke(_player, null);
        }
        catch { /* ignore */ }

        DisposePlayerQuiet();
        _removeButtonPressed = null;
        _buttonPressedHandler = null;
        _buttonPressedToken = null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
