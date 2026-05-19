namespace Hermes.DesktopCapture.Models;

public sealed class ScreenCaptureResult
{
    public required string ImagePath { get; init; }

    public required string AnnotatedImagePath { get; init; }

    public required string MetadataPath { get; init; }

    public required MonitorInfo Monitor { get; init; }

    public required IReadOnlyList<ScreenRegion> Regions { get; init; }

    public string? ForegroundWindowTitle { get; init; }

    public int WindowCount { get; init; }

    public string? DuplicateDirectory { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
}
