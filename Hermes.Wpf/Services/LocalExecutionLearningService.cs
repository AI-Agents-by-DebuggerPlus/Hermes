using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Post-local execution hook: memory extraction, vault, role capture, WSL sync, skill crystallize.</summary>
public sealed class LocalExecutionLearningService
{
    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly MemoryExtractorService _memoryExtractor;
    private readonly RoleExperienceCapture _roleCapture;
    private readonly ExternalBrainWriteService _brainWriter;
    private readonly ExternalBrainService _externalBrain;
    private readonly WslAgentMemorySyncService _wslSync;
    private readonly Action<string>? _onSettingsPersist;

    public LocalExecutionLearningService(
        LogService log,
        Func<HermesSettings> settings,
        MemoryExtractorService memoryExtractor,
        RoleExperienceCapture roleCapture,
        ExternalBrainWriteService brainWriter,
        ExternalBrainService externalBrain,
        WslAgentMemorySyncService wslSync,
        Action<string>? onSettingsPersist = null)
    {
        _log = log;
        _settings = settings;
        _memoryExtractor = memoryExtractor;
        _roleCapture = roleCapture;
        _brainWriter = brainWriter;
        _externalBrain = externalBrain;
        _wslSync = wslSync;
        _onSettingsPersist = onSettingsPersist;
    }

    public bool IsEnabled => _settings().LocalLearningLoopEnabled;

    public async Task<MemoryDraft?> ProcessAsync(
        LocalExecutionRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var vault = _externalBrain.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault))
        {
            _log.LogWarn("[local-learning] External Brain path empty — skip vault write (enable MemoryPath in settings)");
        }

        var draft = BuildDraft(record);
        if (!string.IsNullOrWhiteSpace(vault) && !string.IsNullOrWhiteSpace(record.ScreenshotPath))
        {
            var screenshotRel = _brainWriter.TryArchiveScreenshot(vault, record.ScreenshotPath, record.Kind);
            if (screenshotRel is not null)
            {
                draft.Reusable += $"\n\nScreenshot: `{screenshotRel}`";
            }
        }

        if (!string.IsNullOrWhiteSpace(vault))
        {
            var subfolder = record.Kind switch
            {
                LocalAutomationKind.ReniWaterSubmit => Path.Combine("Procedures", "Utilities", "ReniWater"),
                _ => null,
            };

            await _memoryExtractor.ExtractAndSaveAsync(
                draft,
                vault,
                _brainWriter,
                subfolder,
                cancellationToken).ConfigureAwait(false);
        }

        var captureRole = ResolveCaptureRole(record);
        var captureOptions = new LocalCaptureOptions
        {
            BypassMinLength = true,
            MinImportanceOverride = 4,
            ForceRoleCaptureWhenDisabled = record.Success && record.Kind == LocalAutomationKind.ReniWaterSubmit,
        };

        if (!string.IsNullOrWhiteSpace(vault))
        {
            var captured = await _roleCapture
                .CaptureIfNeededAsync(draft, captureRole, vault, captureOptions)
                .ConfigureAwait(false);
            if (captured)
            {
                _externalBrain.RestartWatcherAndReload("local-role-capture");
            }
        }

        if (record.Success && record.Kind == LocalAutomationKind.ReniWaterSubmit)
        {
            var s = _settings();
            s.ReniWaterLearningSuccessCount++;
            _onSettingsPersist?.Invoke("reni-water-learning");
            _log.LogInfo($"[local-learning] Reni success count={s.ReniWaterLearningSuccessCount}");
        }

        try
        {
            _wslSync.TrySync(_settings(), _externalBrain);
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[local-learning] WSL sync: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(vault))
        {
            _externalBrain.RestartWatcherAndReload("local-learning");
        }

        return draft;
    }

    private MemoryDraft BuildDraft(LocalExecutionRecord record) =>
        record.Kind switch
        {
            LocalAutomationKind.ReniWaterSubmit => ReniWaterExperienceBuilder.BuildSubmitDraft(
                record,
                _settings(),
                null),
            _ => _memoryExtractor.ExtractFromLocalExecution(record),
        };

    private static AgentRole ResolveCaptureRole(LocalExecutionRecord record) =>
        record.Kind switch
        {
            LocalAutomationKind.ReniWaterSubmit
                or LocalAutomationKind.ReniWaterAck
                or LocalAutomationKind.ReniWaterSchedule
                or LocalAutomationKind.ReniWaterSessionCheck => AgentRole.UtilitiesManager,
            _ => AgentRole.PersonalManager,
        };
}
