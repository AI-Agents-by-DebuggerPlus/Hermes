using System;
using System.Runtime.InteropServices;

namespace WpfTestApp
{
    internal static class SystemDisplayPower
    {
        private const int HwndBroadcast = 0xFFFF;
        private const uint WmSysCommand = 0x0112;
        private const int ScMonitorPower = 0xF170;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static void TurnOffMonitors()
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
                PostMessage(fg, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);

            PostMessage((IntPtr)HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);
            SendMessage((IntPtr)HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);
        }

        public static void TurnOnMonitors()
        {
            PostMessage((IntPtr)HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)(-1));
            SendMessage((IntPtr)HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)(-1));
        }
    }
}
