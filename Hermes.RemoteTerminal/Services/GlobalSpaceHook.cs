using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Hermes.RemoteTerminal.Services;

/// <summary>
/// Low-level WH_KEYBOARD_LL hook for Space — works even when WPF window lost focus after monitor off.
/// </summary>
public sealed class GlobalSpaceHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int VkSpace = 0x20;

    private readonly Dispatcher _dispatcher;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // keep alive for GC

    public event Action? SpacePressed;

    public GlobalSpaceHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _proc = HookCallback;
        using var cur = Process.GetCurrentProcess();
        using var mod = cur.MainModule!;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(mod.ModuleName), 0);
        if (_hook == IntPtr.Zero)
        {
            AppLog.Warn("GlobalSpaceHook: SetWindowsHookEx failed err=" + Marshal.GetLastWin32Error());
            _proc = null;
            return;
        }

        AppLog.Info("GlobalSpaceHook: installed (Space)");
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
        AppLog.Info("GlobalSpaceHook: removed");
    }

    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg is WmKeydown or WmSyskeydown)
            {
                var vk = Marshal.ReadInt32(lParam);
                if (vk == VkSpace)
                {
                    AppLog.Info("GlobalSpaceHook: Space down");
                    try
                    {
                        _dispatcher.BeginInvoke(() => SpacePressed?.Invoke(), DispatcherPriority.Send);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("GlobalSpaceHook invoke: " + ex.Message);
                    }
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
