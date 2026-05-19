using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class MonitorCapture
{
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var screens = Screen.AllScreens;
        var list = new List<MonitorInfo>(screens.Length);
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            list.Add(
                new MonitorInfo
                {
                    Index = i,
                    DeviceName = s.DeviceName,
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    IsPrimary = s.Primary,
                });
        }

        return list;
    }

    public static MonitorInfo GetPrimaryMonitor()
    {
        var monitors = EnumerateMonitors();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    public static MonitorInfo ResolveMonitor(int monitorIndex)
    {
        var monitors = EnumerateMonitors();
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No displays found.");
        }

        if (monitorIndex < 0)
        {
            return GetPrimaryMonitor();
        }

        if (monitorIndex >= monitors.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monitorIndex),
                monitorIndex,
                $"Monitor index must be 0..{monitors.Count - 1}.");
        }

        return monitors[monitorIndex];
    }

    public static void CaptureToFile(MonitorInfo monitor, string outputPath)
    {
        var bounds = monitor.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }
}
