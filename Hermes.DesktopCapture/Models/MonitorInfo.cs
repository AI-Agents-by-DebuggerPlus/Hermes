namespace Hermes.DesktopCapture.Models;

public sealed class MonitorInfo
{
    public required int Index { get; init; }

    public required string DeviceName { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required bool IsPrimary { get; init; }

    public System.Drawing.Rectangle Bounds => new(X, Y, Width, Height);
}
