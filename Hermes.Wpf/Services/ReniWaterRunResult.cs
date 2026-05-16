namespace Hermes.Wpf.Services;

public sealed class ReniWaterRunResult
{
    public int ExitCode { get; init; }
    public string CombinedText { get; init; } = string.Empty;
    public bool Success => ExitCode == 0;
    public bool AuthRequired { get; init; }
    public bool SubmitAccepted { get; init; }
    public string? ScreenshotPath { get; init; }
}
