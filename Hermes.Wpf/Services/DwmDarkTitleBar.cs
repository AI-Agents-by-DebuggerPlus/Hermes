using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.Wpf.Services;

/// <summary>
/// Enables the Windows 10/11 dark title bar (non-client chrome) to match Hermes Command Center.
/// </summary>
public static class DwmDarkTitleBar
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
        catch
        {
            // Non-fatal: window remains usable even if dark title bar fails.
        }
    }

    public static void RegisterForAllWindows()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded),
            handledEventsToo: true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized += (_, _) => Apply(window);
        Apply(window);
    }
}
