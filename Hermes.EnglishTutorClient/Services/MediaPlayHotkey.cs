using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Global Play/Pause capture: RegisterHotKey + low-level keyboard hook fallback.
/// Needed when another app already owns VK_MEDIA_PLAY_PAUSE (win32=1409).
/// </summary>
public sealed class MediaPlayHotkey : IDisposable
{
    public const int DefaultHotkeyId = 0xE211; // unique vs EnglishLearning 0xE101
    private const uint VkMediaPlayPause = 0xB3;
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int WmHotkey = 0x0312;

    private readonly Window _window;
    private readonly int _hotkeyId;
    private readonly string _tag;
    private HwndSource? _source;
    private bool _hotkeyRegistered;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc; // keep alive
    private bool _disposed;
    private bool _installLlHook;

    public event Action? PlayPausePressed;
    public bool IsRegistered => _hotkeyRegistered || _hook != IntPtr.Zero;
    public int HotkeyIdValue => _hotkeyId;

    public MediaPlayHotkey(Window window, int hotkeyId = DefaultHotkeyId, bool installLlHook = true, string tag = "MediaPlay")
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _hotkeyId = hotkeyId;
        _installLlHook = installLlHook;
        _tag = string.IsNullOrWhiteSpace(tag) ? "MediaPlay" : tag;
        _window.SourceInitialized += OnSourceInitialized;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
            Attach(hwnd);
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        Attach(new WindowInteropHelper(_window).Handle);

    private void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _disposed) return;

        if (_source == null)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
            AppLog.Info(_tag + ": HwndSource hook attached hwnd=0x" + hwnd.ToString("X"));
        }

        TryRegisterHotkey(hwnd);
        if (_installLlHook)
            TryInstallKeyboardHook();
    }

    private void TryRegisterHotkey(IntPtr hwnd)
    {
        if (_hotkeyRegistered) return;
        if (RegisterHotKey(hwnd, _hotkeyId, 0x4000, VkMediaPlayPause))
        {
            _hotkeyRegistered = true;
            AppLog.Info(_tag + ": RegisterHotKey OK id=0x" + _hotkeyId.ToString("X"));
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            AppLog.Warn(_tag + ": RegisterHotKey failed win32=" + err
                + " (often 1409=already taken) — using LL keyboard hook");
        }
    }

    private void TryInstallKeyboardHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hookProc = HookCallback;
        using var cur = Process.GetCurrentProcess();
        using var mod = cur.MainModule;
        var hMod = mod != null ? GetModuleHandle(mod.ModuleName) : IntPtr.Zero;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, hMod, 0);
        if (_hook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            AppLog.Error(_tag + ": SetWindowsHookEx WH_KEYBOARD_LL failed win32=" + err);
        }
        else
        {
            AppLog.Info(_tag + ": WH_KEYBOARD_LL installed");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            AppLog.Info(_tag + ": WM_HOTKEY received");
            Raise("RegisterHotKey");
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
            {
                var vk = Marshal.ReadInt32(lParam);
                if (vk == (int)VkMediaPlayPause)
                {
                    AppLog.Info(_tag + ": LL hook VK_MEDIA_PLAY_PAUSE");
                    try
                    {
                        _window.Dispatcher.BeginInvoke(new Action(() => Raise("LL-hook")));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn(_tag + ": dispatch Raise failed: " + ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(_tag + ": hook callback: " + ex.Message);
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public event Action<string>? PlayPausePressedWithSource;

    private void Raise(string source)
    {
        try { PlayPausePressedWithSource?.Invoke(source); } catch { /* ignore */ }
        try { PlayPausePressed?.Invoke(); }
        catch (Exception ex) { AppLog.Warn(_tag + ": handler: " + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hotkeyRegistered)
        {
            try
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                UnregisterHotKey(hwnd, _hotkeyId);
            }
            catch { /* ignore */ }
            _hotkeyRegistered = false;
        }

        if (_hook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_hook); } catch { /* ignore */ }
            _hook = IntPtr.Zero;
        }

        _hookProc = null;
        if (_source != null)
        {
            try { _source.RemoveHook(WndProc); } catch { /* ignore */ }
            _source = null;
        }

        AppLog.Info(_tag + ": disposed");
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
