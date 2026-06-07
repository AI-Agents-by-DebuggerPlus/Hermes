using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Unified Reni Water submit path: Playwright script + post-local learning loop.</summary>
public sealed class ReniWaterExecutionCoordinator
{
    private readonly ReniWaterScriptService _script;
    private readonly LocalExecutionLearningService _learning;
    private readonly LogService _log;

    public ReniWaterExecutionCoordinator(
        ReniWaterScriptService script,
        LocalExecutionLearningService learning,
        LogService log)
    {
        _script = script;
        _learning = learning;
        _log = log;
    }

    public async Task<ReniWaterRunResult> RunSubmitAsync(
        string userTask,
        string triggerSource,
        string? projectName,
        CancellationToken cancellationToken = default)
    {
        _log.LogInfo($"[reni-water] coordinator submit source={triggerSource}");
        var result = await _script.RunSubmitAsync(cancellationToken).ConfigureAwait(false);

        var success = result.SubmitAccepted || (result.Success && !result.AuthRequired);
        var summary = BuildAssistantSummary(result);

        if (_learning.IsEnabled)
        {
            var record = new LocalExecutionRecord
            {
                Kind = LocalAutomationKind.ReniWaterSubmit,
                UserTask = userTask,
                AssistantSummary = summary,
                Success = success,
                TriggerSource = triggerSource,
                ProjectName = projectName,
                ScreenshotPath = result.ScreenshotPath,
                ReniResult = result,
            };

            await _learning.ProcessAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static string BuildAssistantSummary(ReniWaterRunResult result)
    {
        if (result.AuthRequired)
        {
            return ReniWaterUserMessages.AuthRequired;
        }

        if (result.SubmitAccepted)
        {
            return ReniWaterUserMessages.SubmitSuccess;
        }

        if (result.CombinedText.Contains("SUBMIT_NOT_ACCEPTED", StringComparison.OrdinalIgnoreCase))
        {
            return ReniWaterUserMessages.SubmitNotAccepted;
        }

        if (result.Success)
        {
            return ReniWaterUserMessages.SubmitSuccess;
        }

        return $"Ошибка передачи показаний (код {result.ExitCode}).";
    }
}
