using System.Runtime.InteropServices;

namespace Hermes.DesktopInteraction;

/// <summary>Low-level Windows cursor positioning and synthesized mouse clicks (virtual screen).</summary>
public static class DesktopMouse
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    public static bool TryGetCursorPos(out CursorPoint point)
    {
        if (!Native.GetCursorPos(out var p))
        {
            point = default;
            return false;
        }

        point = new CursorPoint(p.X, p.Y);
        return true;
    }

    public static bool ClipToVirtualScreen(ref int x, ref int y)
    {
        try
        {
            var lx = Native.GetSystemMetrics(SmXVirtualScreen);
            var ly = Native.GetSystemMetrics(SmYVirtualScreen);
            var w = Native.GetSystemMetrics(SmCxVirtualScreen);
            var h = Native.GetSystemMetrics(SmCyVirtualScreen);
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            var r = lx + w - 1;
            var b = ly + h - 1;
            if (x < lx)
            {
                x = lx;
            }
            else if (x > r)
            {
                x = r;
            }

            if (y < ly)
            {
                y = ly;
            }
            else if (y > b)
            {
                y = b;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool MoveTo(int screenX, int screenY)
    {
        ClipToVirtualScreen(ref screenX, ref screenY);
        return Native.SetCursorPos(screenX, screenY);
    }

    /// <summary>Delta from current position (pixels), clipped to virtual screen.</summary>
    public static bool MoveBy(int dx, int dy)
    {
        if (!TryGetCursorPos(out var p))
        {
            return false;
        }

        return MoveTo(p.X + dx, p.Y + dy);
    }

    public static bool LeftClick(int repeat = 1, int delayBetweenDownUpMs = 6, int gapBetweenSequenceMs = 52)
    {
        repeat = Math.Clamp(repeat, 1, 5);
        for (var i = 0; i < repeat; i++)
        {
            Native.mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            if (delayBetweenDownUpMs > 0)
            {
                Thread.Sleep(delayBetweenDownUpMs);
            }

            Native.mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            if (i < repeat - 1 && gapBetweenSequenceMs > 0)
            {
                Thread.Sleep(gapBetweenSequenceMs);
            }
        }

        return true;
    }

    public static bool RightClick(int delayBetweenDownUpMs = 6)
    {
        Native.mouse_event(MouseEventRightDown, 0, 0, 0, UIntPtr.Zero);
        if (delayBetweenDownUpMs > 0)
        {
            Thread.Sleep(delayBetweenDownUpMs);
        }

        Native.mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);
        return true;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
    }
}

/// <summary>Screen coordinates (pixels).</summary>
public readonly record struct CursorPoint(int X, int Y);
