using Hermes.Wpf.Models;
using Hermes.Wpf.Skills;

namespace Hermes.Wpf.Services;

/// <summary>Executes Windows-local actions requested by Hermes CLI via wpf_local JSON.</summary>
public sealed class WpfLocalActionExecutor
{
    private readonly Func<HermesSettings> _settings;
    private readonly ReniWaterExecutionCoordinator _reniCoordinator;
    private readonly ReniWaterScriptService _reniScript;
    private readonly ReniWaterSchTasksService _schTasks;
    private readonly LocalExecutionLearningService _learning;

    public WpfLocalActionExecutor(
        Func<HermesSettings> settings,
        ReniWaterExecutionCoordinator reniCoordinator,
        ReniWaterScriptService reniScript,
        ReniWaterSchTasksService schTasks,
        LocalExecutionLearningService learning)
    {
        _settings = settings;
        _reniCoordinator = reniCoordinator;
        _reniScript = reniScript;
        _schTasks = schTasks;
        _learning = learning;
    }

    public async Task<WpfLocalActionResult> ExecuteAsync(
        WpfLocalIntent intent,
        string? projectName,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken = default)
    {
        return intent.Action switch
        {
            "reni_water_submit" => await ExecuteReniSubmitAsync(projectName, userTask, triggerSource, cancellationToken)
                .ConfigureAwait(false),
            "reni_water_ack" => await ExecuteReniAckAsync(projectName, userTask, triggerSource, cancellationToken)
                .ConfigureAwait(false),
            "reni_water_login" => ExecuteReniLogin(userTask),
            "reni_water_check_session" => await ExecuteReniCheckSessionAsync(
                    projectName,
                    userTask,
                    triggerSource,
                    cancellationToken)
                .ConfigureAwait(false),
            "reni_water_status" => await ExecuteReniStatusAsync(userTask, triggerSource, cancellationToken)
                .ConfigureAwait(false),
            "reni_water_schtasks_register" => await ExecuteSchTasksRegisterAsync(
                    userTask,
                    triggerSource,
                    cancellationToken)
                .ConfigureAwait(false),
            "reni_water_schtasks_unregister" => await ExecuteSchTasksUnregisterAsync(
                    userTask,
                    triggerSource,
                    cancellationToken)
                .ConfigureAwait(false),
            "reni_water_schedule" => await ExecuteLegacyScheduleAsync(intent, userTask, triggerSource, cancellationToken)
                .ConfigureAwait(false),
            _ => new WpfLocalActionResult
            {
                Ok = false,
                Action = intent.Action,
                UserMessage = $"[wpf-local] Неизвестное действие: {intent.Action}",
            },
        };
    }

    private async Task<WpfLocalActionResult> ExecuteReniSubmitAsync(
        string? projectName,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var result = await _reniCoordinator
            .RunSubmitAsync(userTask, triggerSource, projectName, cancellationToken)
            .ConfigureAwait(false);

        var success = result.SubmitAccepted || (result.Success && !result.AuthRequired);
        var message = BuildSubmitMessage(result);

        return new WpfLocalActionResult
        {
            Ok = success,
            Action = "reni_water_submit",
            UserMessage = message,
            ScreenshotPath = result.ScreenshotPath,
            LearningRecord = _learning.IsEnabled
                ? new LocalExecutionRecord
                {
                    Kind = LocalAutomationKind.ReniWaterSubmit,
                    UserTask = userTask,
                    AssistantSummary = message,
                    Success = success,
                    TriggerSource = triggerSource,
                    ProjectName = projectName,
                    ScreenshotPath = result.ScreenshotPath,
                    ReniResult = result,
                }
                : null,
        };
    }

    private async Task<WpfLocalActionResult> ExecuteReniAckAsync(
        string? projectName,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var result = await _reniScript.RunAckAsync(cancellationToken).ConfigureAwait(false);
        var ok = result.Success || result.CombinedText.Contains("ACK_OK", StringComparison.Ordinal);
        var message = ok ? ReniWaterUserMessages.AckSuccess : "Не удалось подтвердить уведомление.";

        var record = new LocalExecutionRecord
        {
            Kind = LocalAutomationKind.ReniWaterAck,
            UserTask = userTask,
            AssistantSummary = message,
            Success = ok,
            TriggerSource = triggerSource,
            ProjectName = projectName,
        };

        if (_learning.IsEnabled)
        {
            await _learning.ProcessAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return new WpfLocalActionResult
        {
            Ok = ok,
            Action = "reni_water_ack",
            UserMessage = message,
            LearningRecord = record,
        };
    }

    private WpfLocalActionResult ExecuteReniLogin(string userTask)
    {
        try
        {
            _reniScript.OpenLoginConsole();
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "reni_water_login",
                UserMessage =
                    "Открыта консоль для входа на my.renivodokanal.od.ua. Войдите в браузере, затем повторите передачу показаний.",
            };
        }
        catch (Exception ex)
        {
            return new WpfLocalActionResult
            {
                Ok = false,
                Action = "reni_water_login",
                UserMessage = $"[reni-water] Ошибка входа: {ex.Message}",
            };
        }
    }

    private async Task<WpfLocalActionResult> ExecuteReniCheckSessionAsync(
        string? projectName,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var result = await _reniScript.RunCheckSessionAsync(cancellationToken).ConfigureAwait(false);
        var ok = result.Success && !result.AuthRequired;
        var message = result.AuthRequired
            ? ReniWaterUserMessages.AuthRequired
            : ok
                ? "Сессия Reni Water активна."
                : $"Проверка сессии не удалась (код {result.ExitCode}).";

        var record = new LocalExecutionRecord
        {
            Kind = LocalAutomationKind.ReniWaterSessionCheck,
            UserTask = userTask,
            AssistantSummary = message,
            Success = ok,
            TriggerSource = triggerSource,
            ProjectName = projectName,
            ScreenshotPath = result.ScreenshotPath,
            ReniResult = result,
        };

        if (_learning.IsEnabled)
        {
            await _learning.ProcessAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return new WpfLocalActionResult
        {
            Ok = ok,
            Action = "reni_water_check_session",
            UserMessage = message,
            ScreenshotPath = result.ScreenshotPath,
            LearningRecord = record,
        };
    }

    private async Task<WpfLocalActionResult> ExecuteReniStatusAsync(
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var settings = _settings();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ReniWaterExperienceBuilder.BuildStatusText(settings, settings.ReniWaterLearningSuccessCount));
        sb.AppendLine();
        sb.AppendLine(_schTasks.DescribeTasksStatus());

        var message = sb.ToString().TrimEnd();
        return await RecordScheduleResultAsync(
            "reni_water_status",
            userTask,
            triggerSource,
            ok: true,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WpfLocalActionResult> ExecuteSchTasksRegisterAsync(
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        _schTasks.ClearDeprecatedInAppScheduleFields();
        var (ok, detail) = await _schTasks.RegisterDefaultTasksAsync(cancellationToken).ConfigureAwait(false);
        var message = ok
            ? $"[schtasks] Задачи зарегистрированы (Hermes CLI → wpf_local).\n{detail}"
            : $"[schtasks] Не удалось зарегистрировать (нужны права администратора?): {detail}";

        return await RecordScheduleResultAsync(
            "reni_water_schtasks_register",
            userTask,
            triggerSource,
            ok,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WpfLocalActionResult> ExecuteSchTasksUnregisterAsync(
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var (ok, detail) = _schTasks.UnregisterTasks();
        var message = ok
            ? $"[schtasks] Задачи удалены.\n{detail}"
            : $"[schtasks] Ошибка удаления: {detail}";

        return await RecordScheduleResultAsync(
            "reni_water_schtasks_unregister",
            userTask,
            triggerSource,
            ok,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Legacy schedule_action → schtasks (in-app timers removed).</summary>
    private Task<WpfLocalActionResult> ExecuteLegacyScheduleAsync(
        WpfLocalIntent intent,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        if (!TryBuildScheduleRequest(intent, userTask, out var request))
        {
            return Task.FromResult(new WpfLocalActionResult
            {
                Ok = false,
                Action = "reni_water_schedule",
                UserMessage =
                    "In-app расписание Hermes.Wpf отключено. "
                    + "Используй reni_water_schtasks_register/unregister или schtasks через Hermes CLI.",
            });
        }

        return request.Action switch
        {
            ReniWaterScheduleAction.Status => ExecuteReniStatusAsync(userTask, triggerSource, cancellationToken),
            ReniWaterScheduleAction.Cancel => ExecuteSchTasksUnregisterAsync(userTask, triggerSource, cancellationToken),
            ReniWaterScheduleAction.Monthly => ExecuteSchTasksRegisterAsync(userTask, triggerSource, cancellationToken),
            ReniWaterScheduleAction.Once => Task.FromResult(new WpfLocalActionResult
            {
                Ok = false,
                Action = "reni_water_schedule",
                UserMessage =
                    "Разовые задачи: Hermes CLI должен создать schtasks /Create через свой terminal tool "
                    + "(WPF не планирует autonomously).",
            }),
            _ => Task.FromResult(new WpfLocalActionResult
            {
                Ok = false,
                Action = "reni_water_schedule",
                UserMessage = "Неизвестный schedule_action.",
            }),
        };
    }

    private async Task<WpfLocalActionResult> RecordScheduleResultAsync(
        string action,
        string userTask,
        string triggerSource,
        bool ok,
        string message,
        CancellationToken cancellationToken)
    {
        var record = new LocalExecutionRecord
        {
            Kind = LocalAutomationKind.ReniWaterSchedule,
            UserTask = userTask,
            AssistantSummary = message,
            Success = ok,
            TriggerSource = triggerSource,
        };

        if (_learning.IsEnabled)
        {
            await _learning.ProcessAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return new WpfLocalActionResult
        {
            Ok = ok,
            Action = action,
            UserMessage = message,
            LearningRecord = record,
        };
    }

    private static bool TryBuildScheduleRequest(
        WpfLocalIntent intent,
        string userTask,
        out ReniWaterScheduleRequest request)
    {
        request = ReniWaterScheduleRequest.DefaultMonthly();

        if (!string.IsNullOrWhiteSpace(intent.ScheduleAction))
        {
            request = intent.ScheduleAction switch
            {
                "cancel" => new ReniWaterScheduleRequest(ReniWaterScheduleAction.Cancel, null, 1, 5, 9, 0),
                "status" => new ReniWaterScheduleRequest(ReniWaterScheduleAction.Status, null, 1, 5, 9, 0),
                "once" => new ReniWaterScheduleRequest(
                    ReniWaterScheduleAction.Once,
                    intent.RunAtLocal ?? DateTime.Now.AddHours(1),
                    1,
                    5,
                    intent.Hour ?? 9,
                    intent.Minute ?? 0),
                "monthly" => new ReniWaterScheduleRequest(
                    ReniWaterScheduleAction.Monthly,
                    null,
                    intent.WindowStartDay ?? ReniWaterScheduleDefaults.DefaultWindowStartDay,
                    intent.WindowEndDay ?? ReniWaterScheduleDefaults.DefaultWindowEndDay,
                    intent.Hour ?? ReniWaterScheduleDefaults.DefaultHour,
                    intent.Minute ?? ReniWaterScheduleDefaults.DefaultMinute),
                _ => request,
            };
            return request.Action != ReniWaterScheduleAction.None;
        }

        var context = !string.IsNullOrWhiteSpace(intent.UserContext) ? intent.UserContext : userTask;
        return ReniWaterScheduleParser.TryParse(context, out request);
    }

    private static string BuildSubmitMessage(ReniWaterRunResult result)
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
