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
    private readonly AgentTaskSchedulerService? _agentScheduler;
    private readonly PortfolioStoreService? _portfolio;

    public WpfLocalActionExecutor(
        Func<HermesSettings> settings,
        ReniWaterExecutionCoordinator reniCoordinator,
        ReniWaterScriptService reniScript,
        ReniWaterSchTasksService schTasks,
        LocalExecutionLearningService learning,
        AgentTaskSchedulerService? agentScheduler = null,
        PortfolioStoreService? portfolio = null)
    {
        _settings = settings;
        _reniCoordinator = reniCoordinator;
        _reniScript = reniScript;
        _schTasks = schTasks;
        _learning = learning;
        _agentScheduler = agentScheduler;
        _portfolio = portfolio;
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
            "scheduler_add" => ExecuteSchedulerAdd(intent, projectName),
            "scheduler_list" => ExecuteSchedulerList(),
            "scheduler_complete" => ExecuteSchedulerComplete(intent),
            "scheduler_remove" or "scheduler_cancel" => ExecuteSchedulerRemove(intent),
            "portfolio_add" => ExecutePortfolioAdd(intent),
            "portfolio_list" => ExecutePortfolioList(),
            "portfolio_set_status" => ExecutePortfolioSetStatus(intent),
            "portfolio_remove" => ExecutePortfolioRemove(intent),
            _ => new WpfLocalActionResult
            {
                Ok = false,
                Action = intent.Action,
                UserMessage = $"[wpf-local] Неизвестное действие: {intent.Action}",
            },
        };
    }

    private WpfLocalActionResult ExecuteSchedulerAdd(WpfLocalIntent intent, string? projectName)
    {
        if (_agentScheduler is null)
        {
            return Fail("scheduler_add", "Планировщик агентов не инициализирован.");
        }

        var project = string.IsNullOrWhiteSpace(intent.Project) ? projectName : intent.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            return Fail("scheduler_add", "Не указан проект (project).");
        }

        var title = string.IsNullOrWhiteSpace(intent.Title) ? intent.UserContext : intent.Title;
        var command = string.IsNullOrWhiteSpace(intent.Command) ? intent.UserContext : intent.Command;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(command))
        {
            return Fail(
                "scheduler_add",
                "Нужны title и command (текст, который агент получит в чат в due time).");
        }

        DateTime due;
        if (intent.RunAtLocal is { } runAt)
        {
            due = runAt;
        }
        else if (intent.Hour is { } h)
        {
            var now = DateTime.Now;
            due = new DateTime(now.Year, now.Month, now.Day, h, intent.Minute ?? 0, 0);
            if (due <= now)
            {
                due = due.AddDays(1);
            }
        }
        else
        {
            return Fail("scheduler_add", "Укажите run_at (ISO local) или hour/minute.");
        }

        try
        {
            var task = _agentScheduler.Add(
                title,
                command,
                project!,
                due,
                createdBy: project!);
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "scheduler_add",
                UserMessage =
                    $"[scheduler] Задача добавлена в планировщик Hermes.Wpf.\n"
                    + $"id={task.Id}\n"
                    + $"проект={task.ProjectName}\n"
                    + $"когда={task.DueAtLocal:dd.MM.yyyy HH:mm}\n"
                    + $"команда агенту: {task.Command}\n"
                    + "В срок планировщик только напомнит агенту (сам ничего не выполняет). "
                    + "После выполнения удалите: {\"skill\":\"wpf_local\",\"action\":\"scheduler_complete\",\"task_id\":\""
                    + task.Id + "\"}",
            };
        }
        catch (Exception ex)
        {
            return Fail("scheduler_add", ex.Message);
        }
    }

    private WpfLocalActionResult ExecuteSchedulerList()
    {
        if (_agentScheduler is null)
        {
            return Fail("scheduler_list", "Планировщик агентов не инициализирован.");
        }

        var active = _agentScheduler.GetActive();
        if (active.Count == 0)
        {
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "scheduler_list",
                UserMessage = "[scheduler] Активных задач нет.",
            };
        }

        var lines = active.Select(t =>
            $"• {t.Id} | {t.Status} | {t.DueAtLocal:dd.MM HH:mm} | {t.ProjectName} | {t.Title}");
        return new WpfLocalActionResult
        {
            Ok = true,
            Action = "scheduler_list",
            UserMessage = "[scheduler] Активные задачи:\n" + string.Join("\n", lines),
        };
    }

    private WpfLocalActionResult ExecuteSchedulerComplete(WpfLocalIntent intent)
    {
        if (_agentScheduler is null)
        {
            return Fail("scheduler_complete", "Планировщик агентов не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(intent.TaskId))
        {
            return Fail("scheduler_complete", "Нужен task_id.");
        }

        if (!_agentScheduler.TryComplete(intent.TaskId, out var task) || task is null)
        {
            return Fail("scheduler_complete", $"Задача {intent.TaskId} не найдена.");
        }

        return new WpfLocalActionResult
        {
            Ok = true,
            Action = "scheduler_complete",
            UserMessage = $"[scheduler] Задача «{task.Title}» ({task.Id}) отмечена выполненной.",
        };
    }

    private WpfLocalActionResult ExecuteSchedulerRemove(WpfLocalIntent intent)
    {
        if (_agentScheduler is null)
        {
            return Fail("scheduler_remove", "Планировщик агентов не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(intent.TaskId))
        {
            return Fail("scheduler_remove", "Нужен task_id.");
        }

        if (_agentScheduler.TryCancel(intent.TaskId, out var task) && task is not null)
        {
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "scheduler_remove",
                UserMessage = $"[scheduler] Задача «{task.Title}» ({task.Id}) отменена.",
            };
        }

        if (_agentScheduler.TryRemove(intent.TaskId))
        {
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "scheduler_remove",
                UserMessage = $"[scheduler] Задача {intent.TaskId} удалена.",
            };
        }

        return Fail("scheduler_remove", $"Задача {intent.TaskId} не найдена.");
    }

    private WpfLocalActionResult ExecutePortfolioAdd(WpfLocalIntent intent)
    {
        if (_portfolio is null)
        {
            return Fail("portfolio_add", "Portfolio store не инициализирован.");
        }

        var title = string.IsNullOrWhiteSpace(intent.Title) ? intent.UserContext : intent.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            return Fail("portfolio_add", "Нужен title.");
        }

        if (!TryParsePortfolioCategory(intent.Status, out var cat))
        {
            cat = PortfolioCategory.Idea;
        }

        try
        {
            var item = _portfolio.Add(title, intent.Notes, cat, intent.Project);
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "portfolio_add",
                UserMessage =
                    $"[portfolio] Добавлено: «{item.Title}» id={item.Id} ({item.CategoryLabel})"
                    + (string.IsNullOrWhiteSpace(item.LinkedWorkspace)
                        ? ""
                        : $" → workspace {item.LinkedWorkspace}"),
            };
        }
        catch (Exception ex)
        {
            return Fail("portfolio_add", ex.Message);
        }
    }

    private WpfLocalActionResult ExecutePortfolioList()
    {
        if (_portfolio is null)
        {
            return Fail("portfolio_list", "Portfolio store не инициализирован.");
        }

        var all = _portfolio.GetAll();
        if (all.Count == 0)
        {
            return new WpfLocalActionResult
            {
                Ok = true,
                Action = "portfolio_list",
                UserMessage = "[portfolio] Пусто.",
            };
        }

        var lines = all.Select(i =>
            $"• {i.Id} | {i.CategoryLabel} | {i.Title}"
            + (string.IsNullOrWhiteSpace(i.LinkedWorkspace) ? "" : $" → {i.LinkedWorkspace}"));
        return new WpfLocalActionResult
        {
            Ok = true,
            Action = "portfolio_list",
            UserMessage = "[portfolio]\n" + string.Join("\n", lines),
        };
    }

    private WpfLocalActionResult ExecutePortfolioSetStatus(WpfLocalIntent intent)
    {
        if (_portfolio is null)
        {
            return Fail("portfolio_set_status", "Portfolio store не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(intent.TaskId) || !TryParsePortfolioCategory(intent.Status, out var cat))
        {
            return Fail("portfolio_set_status", "Нужны task_id и status (idea|in_dev|current|archive).");
        }

        if (!_portfolio.TrySetCategory(intent.TaskId, cat, out var item) || item is null)
        {
            return Fail("portfolio_set_status", $"Не найдено: {intent.TaskId}");
        }

        return new WpfLocalActionResult
        {
            Ok = true,
            Action = "portfolio_set_status",
            UserMessage = $"[portfolio] «{item.Title}» → {item.CategoryLabel}",
        };
    }

    private WpfLocalActionResult ExecutePortfolioRemove(WpfLocalIntent intent)
    {
        if (_portfolio is null)
        {
            return Fail("portfolio_remove", "Portfolio store не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(intent.TaskId))
        {
            return Fail("portfolio_remove", "Нужен task_id.");
        }

        if (!_portfolio.TryRemove(intent.TaskId))
        {
            return Fail("portfolio_remove", $"Не найдено: {intent.TaskId}");
        }

        return new WpfLocalActionResult
        {
            Ok = true,
            Action = "portfolio_remove",
            UserMessage = $"[portfolio] Удалено {intent.TaskId}",
        };
    }

    private static bool TryParsePortfolioCategory(string raw, out PortfolioCategory cat)
    {
        cat = PortfolioCategory.Idea;
        var s = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        switch (s)
        {
            case "idea":
            case "ideas":
                cat = PortfolioCategory.Idea;
                return true;
            case "in_dev":
            case "in_development":
            case "dev":
            case "development":
                cat = PortfolioCategory.InDevelopment;
                return true;
            case "current":
            case "active":
                cat = PortfolioCategory.Current;
                return true;
            case "archive":
            case "archived":
            case "done":
                cat = PortfolioCategory.Archive;
                return true;
            default:
                return false;
        }
    }

    private static WpfLocalActionResult Fail(string action, string message) =>
        new()
        {
            Ok = false,
            Action = action,
            UserMessage = $"[{action.Split('_')[0]}] {message}",
        };

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

        if (success)
        {
            var monthKey = DateTime.Now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            _settings().ReniWaterLastMonthlyRunKey = monthKey;
        }

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

    private async Task<WpfLocalActionResult> ExecuteSchTasksOnceAsync(
        ReniWaterScheduleRequest request,
        string userTask,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        if (request.RunAtLocal is not { } runAt)
        {
            return new WpfLocalActionResult
            {
                Ok = false,
                Action = "reni_water_schedule",
                UserMessage = "Не указано время разовой передачи показаний.",
            };
        }

        var (ok, detail) = await _schTasks.RegisterOnceTaskAsync(runAt, cancellationToken).ConfigureAwait(false);
        var message = ok
            ? $"[schtasks] {detail}"
            : $"[schtasks] Не удалось создать разовую задачу: {detail}";

        return await RecordScheduleResultAsync(
            "reni_water_schedule",
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
            ReniWaterScheduleAction.Once => ExecuteSchTasksOnceAsync(request, userTask, triggerSource, cancellationToken),
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
