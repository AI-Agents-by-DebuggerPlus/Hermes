using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Registers VK_MEDIA_PLAY_PAUSE so headset Play works even before first Speak
/// (otherwise Windows often routes the key to the default media app).
/// </summary>
public sealed class MediaPlayHotkey : IDisposable
{
    public const int HotkeyId = 0xE101;
    private const uint VkMediaPlayPause = 0xB3;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public event Action? PlayPausePressed;

    public bool IsRegistered => _registered;

    public MediaPlayHotkey(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.SourceInitialized += OnSourceInitialized;

        // If constructed from Loaded, SourceInitialized already fired — register now.
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            AttachAndRegister(hwnd);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        AttachAndRegister(hwnd);
    }

    private void AttachAndRegister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_source == null)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
        }

        TryRegister(hwnd);
    }

    private void TryRegister(IntPtr hwnd)
    {
        if (_registered || hwnd == IntPtr.Zero)
        {
            return;
        }

        // MOD_NOREPEAT = 0x4000 — available on Win7+
        if (RegisterHotKey(hwnd, HotkeyId, 0x4000, VkMediaPlayPause))
        {
            _registered = true;
            AppLog.Info("Registered global hotkey VK_MEDIA_PLAY_PAUSE");
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            AppLog.Warn("RegisterHotKey(MEDIA_PLAY_PAUSE) failed win32=" + err + " — will rely on PreviewKeyDown");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            AppLog.Info("Media Play WM_HOTKEY received");
            PlayPausePressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            try
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                UnregisterHotKey(hwnd, HotkeyId);
            }
            catch
            {
            }

            _registered = false;
        }

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
