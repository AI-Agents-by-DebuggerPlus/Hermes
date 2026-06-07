namespace Hermes.Wpf.Models;

public sealed class WpfLocalActionResult
{
    public required bool Ok { get; init; }

    public required string Action { get; init; }

    public required string UserMessage { get; init; }

    public string? ScreenshotPath { get; init; }

    public LocalExecutionRecord? LearningRecord { get; init; }
}
