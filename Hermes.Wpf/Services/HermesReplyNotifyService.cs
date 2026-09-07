using System.Media;
using System.Windows;
using System.Windows.Threading;
using Hermes.Wpf.Views;

namespace Hermes.Wpf.Services;

/// <summary>Sound + non-modal toast when Hermes finishes a chat turn.</summary>
public static class HermesReplyNotifyService
{
    private static HermesReplyToastWindow? _current;

    public static void Notify(
        Window? owner,
        string projectName,
        string replyText,
        bool enabled,
        bool isError = false)
    {
        if (!enabled)
            return;

        void Show()
        {
            try
            {
                if (isError)
                    SystemSounds.Hand.Play();
                else
                    SystemSounds.Asterisk.Play();
            }
            catch
            {
                // ignore sound failures
            }

            try
            {
                _current?.Close();
            }
            catch
            {
                // ignore
            }

            var toast = new HermesReplyToastWindow(
                projectName,
                Truncate(replyText, 280),
                isError);
            _current = toast;
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, toast))
                    _current = null;
            };

            PositionBottomRight(toast, owner);
            toast.Show();

            if (owner is { IsVisible: true })
            {
                try
                {
                    // Nudge attention without stealing focus permanently.
                    if (!owner.IsActive)
                        owner.Flash();
                }
                catch
                {
                    // ignore
                }
            }
        }

        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            Show();
            return;
        }

        if (app.Dispatcher.CheckAccess())
            Show();
        else
            app.Dispatcher.Invoke(Show, DispatcherPriority.Normal);
    }

    private static void PositionBottomRight(Window toast, Window? owner)
    {
        const double margin = 16;
        var area = owner is { IsVisible: true }
            ? GetWindowWorkArea(owner)
            : SystemParameters.WorkArea;

        toast.Left = area.Right - toast.Width - margin;
        toast.Top = area.Bottom - toast.Height - margin;
        if (toast.Left < area.Left)
            toast.Left = area.Left + margin;
        if (toast.Top < area.Top)
            toast.Top = area.Top + margin;
    }

    private static Rect GetWindowWorkArea(Window owner)
    {
        try
        {
            // Prefer the monitor that hosts the main window.
            var source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget is null)
                return SystemParameters.WorkArea;

            var left = owner.Left;
            var top = owner.Top;
            var width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
            var height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
            if (double.IsNaN(left) || double.IsNaN(top) || width <= 0 || height <= 0)
                return SystemParameters.WorkArea;

            // Approximate: use SystemParameters.WorkArea (primary). Good enough for toast.
            return SystemParameters.WorkArea;
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private static string Truncate(string text, int max)
    {
        var t = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (t.Length <= max)
            return t;

        return t[..(max - 1)].TrimEnd() + "…";
    }
}

internal static class WindowFlashExtensions
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);

    public static void Flash(this Window window)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
            FlashWindow(helper.Handle, true);
    }
}
