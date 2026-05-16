namespace Hermes.Wpf.Models;

public sealed class ReniWaterPendingInfo
{
    public string ReadingSubmitted { get; init; } = "?";
    public string ScreenshotPath { get; init; } = string.Empty;
    public string CreatedUtc { get; init; } = string.Empty;
    public bool AuthRequired { get; init; }
    public string Message { get; init; } = string.Empty;
}
