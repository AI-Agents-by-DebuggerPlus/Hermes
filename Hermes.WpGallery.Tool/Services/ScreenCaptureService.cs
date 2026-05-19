using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Hermes.WpGallery.Tool.Models;

namespace Hermes.WpGallery.Tool.Services;

public class ScreenCaptureService : IDisposable
{
    private byte[]? _lastFrame;

    // Returns list of monitor descriptions
    public static List<string> GetMonitors()
    {
        var list = new List<string> { "Все мониторы" };
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var s = Screen.AllScreens[i];
            list.Add($"Монитор {i + 1}  {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (основной)" : "")}");
        }
        return list;
    }

    public CaptureFrame Capture(AppSettings settings)
    {
        var bounds = GetCaptureBounds(settings);

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        // Optional region crop already handled via bounds

        var (data, mime, ext) = Encode(bmp, settings);
        var filename = $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{ext}";

        return new CaptureFrame(data, mime, filename, DateTime.UtcNow, bounds.Width, bounds.Height);
    }

    public bool HasSignificantChange(byte[] newFrame, int threshold)
    {
        if (_lastFrame == null || _lastFrame.Length != newFrame.Length)
        {
            _lastFrame = newFrame;
            return true;
        }
        // Simple byte difference check (fast, works on compressed data length change)
        long diff = 0;
        int  step = Math.Max(1, newFrame.Length / 1000);
        for (int i = 0; i < newFrame.Length; i += step)
            diff += Math.Abs(newFrame[i] - _lastFrame[i]);

        double pct = (double)diff / (newFrame.Length / step) / 255 * 100;
        _lastFrame = newFrame;
        return pct > threshold;
    }

    // ── Private ──────────────────────────────────────────────
    private static Rectangle GetCaptureBounds(AppSettings s)
    {
        Screen[] screens = Screen.AllScreens;

        Rectangle screenBounds = s.MonitorIndex switch
        {
            -1  => GetAllScreensBounds(screens),
            int i when i >= 0 && i < screens.Length => screens[i].Bounds,
            _   => screens[0].Bounds
        };

        // Apply custom region
        if (s.RegionWidth > 0 && s.RegionHeight > 0)
        {
            return new Rectangle(
                screenBounds.X + s.RegionX,
                screenBounds.Y + s.RegionY,
                Math.Min(s.RegionWidth,  screenBounds.Width),
                Math.Min(s.RegionHeight, screenBounds.Height));
        }

        return screenBounds;
    }

    private static Rectangle GetAllScreensBounds(Screen[] screens)
    {
        int l = screens.Min(s => s.Bounds.Left);
        int t = screens.Min(s => s.Bounds.Top);
        int r = screens.Max(s => s.Bounds.Right);
        int b = screens.Max(s => s.Bounds.Bottom);
        return Rectangle.FromLTRB(l, t, r, b);
    }

    private static (byte[] data, string mime, string ext) Encode(Bitmap bmp, AppSettings s)
    {
        using var ms = new MemoryStream();
        switch (s.ImageFormat.ToLower())
        {
            case "png":
                bmp.Save(ms, ImageFormat.Png);
                return (ms.ToArray(), "image/png", "png");

            default: // jpeg
                var jpegEncoder  = ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, (long)s.JpegQuality);
                bmp.Save(ms, jpegEncoder, encoderParams);
                return (ms.ToArray(), "image/jpeg", "jpg");
        }
    }

    public void Dispose() { }
}
