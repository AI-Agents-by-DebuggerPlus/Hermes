using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hermes.RemoteTerminal.Services;

namespace Hermes.RemoteTerminal;

/// <summary>
/// Fullscreen last screenshot: 10s → monitors off (image kept) → Space (global hook) wakes + 10s.
/// </summary>
public partial class ScreenshotViewerWindow : Window
{
    public const int AutoBlankSeconds = 10;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GlobalSpaceHook? _spaceHook;
    private int _secondsLeft;
    private Action<string>? _statusCallback;
    private ImageSource? _lastImage;
    private string _lastLabel = "";
    private bool _asleep;
    private bool _waking;
    /// <summary>Invalidates pending sleep / SC_MONITORPOWER when a new 10s show starts.</summary>
    private int _showGeneration;

    public bool HasLastScreenshot => _lastImage is not null;
    public bool IsAsleep => _asleep;

    public ScreenshotViewerWindow()
    {
        InitializeComponent();
        _timer.Tick += Timer_OnTick;
    }

    public void ShowScreenshot(byte[] pngBytes, string label, Action<string>? statusCallback)
    {
        _statusCallback = statusCallback;
        try
        {
            using var ms = new MemoryStream(pngBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            _lastImage = bmp;
            _lastLabel = label ?? "";
            Title = "HWT Screenshot — " + Path.GetFileName(_lastLabel);
            AppLog.Info("Screenshot viewer: show " + _lastLabel);
        }
        catch (Exception ex)
        {
            txtPath.Text = "Ошибка изображения: " + ex.Message;
            _lastImage = null;
            AppLog.Warn("Screenshot viewer load: " + ex.Message);
            return;
        }

        BeginTenSecondShow("show");
    }

    /// <summary>Re-show cached image for another <see cref="AutoBlankSeconds"/> (повтор / Space).</summary>
    public bool ShowAgain(Action<string>? statusCallback = null, string reason = "replay")
    {
        if (_lastImage is null)
        {
            return false;
        }

        if (statusCallback is not null)
        {
            _statusCallback = statusCallback;
        }

        BeginTenSecondShow(reason);
        return true;
    }

    private void BeginTenSecondShow(string reason)
    {
        _showGeneration++;
        var gen = _showGeneration;
        _asleep = false;
        _waking = false;
        StopSpaceHook();
        _timer.Stop();

        SystemDisplayPower.TurnOnMonitors();
        BringToForeground();
        ApplyLastImageVisible();

        _secondsLeft = AutoBlankSeconds;
        UpdateCountdownUi();
        _timer.Start();

        Status("Screenshot · показ " + AutoBlankSeconds + " с");
        AppLog.Info($"Screenshot viewer: 10s show ({reason}) gen={gen}");
    }

    public bool TryWakeWithSpace()
    {
        if (!_asleep || _lastImage is null || _waking)
        {
            return false;
        }

        return ShowAgain(reason: "global-space");
    }

    private void ApplyLastImageVisible()
    {
        imgView.Source = _lastImage;
        imgView.Visibility = Visibility.Visible;
        blankOverlay.Visibility = Visibility.Collapsed;
        chromeBar.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = null;
        txtPath.Text = _lastLabel;
    }

    private void StartCountdown()
    {
        _timer.Stop();
        _secondsLeft = AutoBlankSeconds;
        UpdateCountdownUi();
        _timer.Start();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        var gen = _showGeneration;
        _secondsLeft--;
        if (_secondsLeft <= 0)
        {
            _timer.Stop();
            if (gen == _showGeneration)
            {
                EnterSleepKeepScreenshot(gen);
            }

            return;
        }

        if (gen == _showGeneration)
        {
            UpdateCountdownUi();
        }
    }

    private void UpdateCountdownUi()
    {
        Status($"Screenshot · мониторы через {_secondsLeft} с");
    }

    private void EnterSleepKeepScreenshot(int gen)
    {
        if (gen != _showGeneration)
        {
            return;
        }

        // Full software blank: hide chrome (countdown / path / close) so only black remains.
        chromeBar.Visibility = Visibility.Collapsed;
        blankOverlay.Visibility = Visibility.Visible;
        imgView.Visibility = Visibility.Visible;
        imgView.Source = _lastImage; // keep last frame under overlay
        _asleep = true;
        Mouse.OverrideCursor = Cursors.None;
        Status("Screenshot · мониторы выкл · ждём Пробел");
        AppLog.Info("Screenshot viewer: asleep (UI blank), requesting monitor power-off gen=" + gen);

        // Paint black frame first; activating the window right before power-off often keeps panels on.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                // Drop stale power-off if a newer show (повтор) already started.
                if (gen != _showGeneration || !_asleep)
                {
                    AppLog.Info($"Screenshot viewer: skip stale TurnOff gen={gen} current={_showGeneration}");
                    return;
                }

                SystemDisplayPower.TurnOffMonitors();
                StartSpaceHook();
                AppLog.Info("Screenshot viewer: SC_MONITORPOWER sent, Space hook on");
            }));
    }

    private void WakeAndShowAgain(string source) => ShowAgain(reason: source);

    private void StartSpaceHook()
    {
        if (_spaceHook is not null)
        {
            return;
        }

        _spaceHook = new GlobalSpaceHook(Dispatcher);
        _spaceHook.SpacePressed += OnGlobalSpace;
        _spaceHook.Start();
    }

    private void StopSpaceHook()
    {
        if (_spaceHook is null)
        {
            return;
        }

        _spaceHook.SpacePressed -= OnGlobalSpace;
        _spaceHook.Dispose();
        _spaceHook = null;
    }

    private void OnGlobalSpace()
    {
        if (!_asleep)
        {
            return;
        }

        ShowAgain(reason: "global-hook");
    }

    private void BringToForeground()
    {
        ApplyFullscreen();
        if (!IsVisible)
        {
            Show();
        }

        try
        {
            Topmost = false;
            Topmost = true;
            Activate();
            Focus();
            Keyboard.Focus(this);

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0040); // NOSIZE|NOMOVE|SHOWWINDOW, HWND_TOPMOST
        }
        catch (Exception ex)
        {
            AppLog.Warn("BringToForeground: " + ex.Message);
        }
    }

    private void ApplyFullscreen()
    {
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Topmost = true;
        ShowInTaskbar = true;
    }

    private void Status(string msg)
    {
        txtCountdown.Text = msg;
        _statusCallback?.Invoke(msg);
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e) => BringToForeground();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            AppLog.Info("Screenshot viewer: Space (WPF KeyDown) asleep=" + _asleep);
            if (_asleep)
            {
                WakeAndShowAgain("wpf-keydown");
            }

            return;
        }

        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        StopSpaceHook();
        Mouse.OverrideCursor = null;
        AppLog.Info("Screenshot viewer: closed");
        _statusCallback?.Invoke("WebSocket: канал messages OK");
        _statusCallback = null;
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == btnClose)
        {
            return;
        }

        if (_asleep)
        {
            e.Handled = true;
            return;
        }

        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
