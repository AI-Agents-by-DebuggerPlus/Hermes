using System.Runtime.InteropServices;
using System.Text;

namespace Hermes.DesktopCapture;

internal static class NativeWindow
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsVisible = 0x10000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal sealed record WindowOnScreen(
        IntPtr Hwnd,
        System.Drawing.Rectangle Bounds,
        string Title,
        string ProcessName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal static string GetWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(512);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static System.Drawing.Rectangle ToDrawingRect(Rect rect) =>
        System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    internal static IReadOnlyList<WindowOnScreen> EnumerateVisibleWindows(System.Drawing.Rectangle monitorBounds)
    {
        var found = new List<WindowOnScreen>();

        EnumWindows(
            (hwnd, _) =>
            {
                if (!ShouldIncludeTopLevelWindow(hwnd, monitorBounds, out var bounds, out var title))
                {
                    return true;
                }

                var processName = WindowDisplayNames.GetProcessName(hwnd);
                found.Add(new WindowOnScreen(hwnd, bounds, title, processName));
                return true;
            },
            IntPtr.Zero);

        return found
            .OrderByDescending(w => w.Bounds.Width * (long)w.Bounds.Height)
            .ToList();
    }

    internal static bool TryGetTaskbarBounds(out System.Drawing.Rectangle bounds)
    {
        bounds = System.Drawing.Rectangle.Empty;
        var hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        bounds = ToDrawingRect(rect);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool ShouldIncludeTopLevelWindow(
        IntPtr hwnd,
        System.Drawing.Rectangle monitorBounds,
        out System.Drawing.Rectangle bounds,
        out string title)
    {
        bounds = System.Drawing.Rectangle.Empty;
        title = string.Empty;

        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var wr))
        {
            return false;
        }

        bounds = ToDrawingRect(wr);
        if (bounds.Width < 80 || bounds.Height < 48)
        {
            return false;
        }

        if (!bounds.IntersectsWith(monitorBounds))
        {
            return false;
        }

        var style = GetWindowLong(hwnd, GwlStyle);
        if ((style & WsVisible) == 0)
        {
            return false;
        }

        title = GetWindowTitle(hwnd);
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((exStyle & WsExToolWindow) != 0 && string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if ((exStyle & WsExNoActivate) != 0 && string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return true;
    }

    private static int GetWindowLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8
            ? (int)GetWindowLongPtr64(hwnd, index)
            : GetWindowLong32(hwnd, index);
}
