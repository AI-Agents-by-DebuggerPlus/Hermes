using Hermes.Wpf.Services;

namespace Hermes.Wpf.Models;

/// <summary>Outcome of a built-in local handler for post-execution learning.</summary>
public sealed class LocalExecutionRecord
{
    public required LocalAutomationKind Kind { get; init; }

    public required string UserTask { get; init; }

    public required string AssistantSummary { get; init; }

    public required bool Success { get; init; }

    public required string TriggerSource { get; init; }

    public string? ProjectName { get; init; }

    public string? ScreenshotPath { get; init; }

    public ReniWaterRunResult? ReniResult { get; init; }
}
