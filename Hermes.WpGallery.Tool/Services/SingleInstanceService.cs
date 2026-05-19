using System.Runtime.InteropServices;
using System.Windows;

namespace Hermes.WpGallery.Tool.Services;

public static class SingleInstanceService
{
    private const string MutexName = "Global\\Hermes.WpGallery.Tool.SingleInstance.Mutex";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        return created;
    }

    public static void ActivateExistingInstance()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess();
        var others = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
            .Where(p => p.Id != current.Id);

        foreach (var proc in others)
        {
            try
            {
                var hWnd = proc.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    hWnd = FindWindowForProcess(proc.Id);
                    if (hWnd == IntPtr.Zero) continue;
                }

                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SwRestore);

                SetForegroundWindow(hWnd);
                return;
            }
            catch { /* next process */ }
            finally { proc.Dispose(); }
        }
    }

    private static IntPtr FindWindowForProcess(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid == processId && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private const int SwRestore = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
