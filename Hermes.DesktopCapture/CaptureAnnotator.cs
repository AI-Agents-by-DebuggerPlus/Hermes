using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Drawing.Imaging;
using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class CaptureAnnotator
{
    public static void SaveAnnotated(
        string sourceImagePath,
        string outputPath,
        MonitorInfo monitor,
        IReadOnlyList<ScreenRegion> regions)
    {
        using var bitmap = (Bitmap)Image.FromFile(sourceImagePath);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var outlinePen = new Pen(Color.FromArgb(220, 255, 220, 80), 2f);
        using var numberFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var numberBack = new SolidBrush(Color.FromArgb(210, 20, 20, 20));
        using var numberFore = Brushes.White;

        foreach (var region in regions)
        {
            if (region.Index <= 0)
            {
                continue;
            }

            var local = ToMonitorLocal(region, monitor);
            if (local.Width <= 0 || local.Height <= 0)
            {
                continue;
            }

            graphics.DrawRectangle(outlinePen, local);

            var text = region.Index.ToString();
            var size = graphics.MeasureString(text, numberFont);
            var pad = 4f;
            var tagW = Math.Max(size.Width + pad * 2, 18f);
            var tagH = Math.Max(size.Height + pad, 16f);
            var tagX = (float)local.Left + 4f;
            var tagY = (float)local.Top + 4f;
            if (tagX + tagW > bitmap.Width)
            {
                tagX = Math.Max(0f, bitmap.Width - tagW);
            }

            if (tagY + tagH > bitmap.Height)
            {
                tagY = Math.Max(0f, bitmap.Height - tagH);
            }

            graphics.FillRectangle(numberBack, tagX, tagY, tagW, tagH);
            graphics.DrawString(text, numberFont, numberFore, tagX + pad, tagY + 1);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static Rectangle ToMonitorLocal(ScreenRegion region, MonitorInfo monitor)
    {
        var rect = region.ToRectangle();
        rect.Offset(-monitor.X, -monitor.Y);
        var bounds = new Rectangle(0, 0, monitor.Width, monitor.Height);
        return Rectangle.Intersect(bounds, rect);
    }
}
