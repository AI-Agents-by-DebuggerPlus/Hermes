using Hermes.Wpf.Models;
using Hermes.Wpf.Skills;

namespace Hermes.Wpf.Services;

/// <summary>Intercepts Reni Water chat phrases before Hermes CLI (works in pure CLI mode too).</summary>
public sealed class ReniWaterLocalChatHandler
{
    private readonly ReniWaterScriptService _script;
    private readonly WpfLocalActionExecutor _executor;
    private readonly LogService _log;

    public ReniWaterLocalChatHandler(
        ReniWaterScriptService script,
        WpfLocalActionExecutor executor,
        LogService log)
    {
        _script = script;
        _executor = executor;
        _log = log;
    }

    public async Task<ReniWaterLocalHandleResult?> TryHandleAsync(
        string userPayload,
        string? projectName,
        CancellationToken cancellationToken = default)
    {
        var text = (userPayload ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (ReniWaterStatusTriggers.Matches(text))
        {
            _log.LogInfo("[reni-water] local status query");
            var exec = await _executor
                .ExecuteAsync(
                    new WpfLocalIntent { Action = "reni_water_status" },
                    projectName,
                    text,
                    "chat-local-status",
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(exec);
        }

        if (ReniWaterScheduleParser.TryParse(text, out var schedule))
        {
            _log.LogInfo($"[reni-water] local schedule action={schedule.Action}");
            return await HandleScheduleAsync(schedule, projectName, text, cancellationToken).ConfigureAwait(false);
        }

        var pending = _script.ReadPendingAck();

        if (ReniWaterSubmitTriggers.MatchesSubmit(text))
        {
            _log.LogInfo("[reni-water] local submit");
            var exec = await _executor
                .ExecuteAsync(
                    new WpfLocalIntent { Action = "reni_water_submit" },
                    projectName,
                    text,
                    "chat-local-submit",
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(exec, reniBusy: true);
        }

        if (ReniWaterAckTriggers.MatchesAck(text, pending is not null))
        {
            _log.LogInfo("[reni-water] local ack");
            var exec = await _executor
                .ExecuteAsync(
                    new WpfLocalIntent { Action = "reni_water_ack" },
                    projectName,
                    text,
                    "chat-local-ack",
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(exec);
        }

        if (ReniWaterSubmitTriggers.MatchesLogin(text))
        {
            _log.LogInfo("[reni-water] local login");
            var exec = await _executor
                .ExecuteAsync(
                    new WpfLocalIntent { Action = "reni_water_login" },
                    projectName,
                    text,
                    "chat-local-login",
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(exec);
        }

        if (MatchesSessionCheck(text))
        {
            _log.LogInfo("[reni-water] local session check");
            var exec = await _executor
                .ExecuteAsync(
                    new WpfLocalIntent { Action = "reni_water_check_session" },
                    projectName,
                    text,
                    "chat-local-session",
                    cancellationToken)
                .ConfigureAwait(false);
            return ToResult(exec, reniBusy: true);
        }

        return null;
    }

    private async Task<ReniWaterLocalHandleResult> HandleScheduleAsync(
        ReniWaterScheduleRequest schedule,
        string? projectName,
        string userTask,
        CancellationToken cancellationToken)
    {
        var intent = schedule.Action switch
        {
            ReniWaterScheduleAction.Cancel => new WpfLocalIntent { Action = "reni_water_schtasks_unregister" },
            ReniWaterScheduleAction.Status => new WpfLocalIntent { Action = "reni_water_status" },
            ReniWaterScheduleAction.Monthly => new WpfLocalIntent { Action = "reni_water_schtasks_register" },
            ReniWaterScheduleAction.Once => new WpfLocalIntent
            {
                Action = "reni_water_schedule",
                ScheduleAction = "once",
                RunAtLocal = schedule.RunAtLocal,
                Hour = schedule.Hour,
                Minute = schedule.Minute,
                UserContext = userTask,
            },
            _ => new WpfLocalIntent { Action = "reni_water_status" },
        };

        var reniBusy = schedule.Action is ReniWaterScheduleAction.Monthly or ReniWaterScheduleAction.Once;
        var exec = await _executor
            .ExecuteAsync(intent, projectName, userTask, "chat-local-schedule", cancellationToken)
            .ConfigureAwait(false);

        var message = exec.UserMessage;
        if (schedule.Action == ReniWaterScheduleAction.Once && exec.Ok && schedule.RunAtLocal is { } runAt)
        {
            message =
                $"Передача показаний запланирована на {runAt:dd.MM.yyyy HH:mm} (локальное время, Windows Task Scheduler).\n"
                + "Показания считываются с сайта автоматически — вводить цифры вручную не нужно.\n"
                + message;
        }
        else if (schedule.Action == ReniWaterScheduleAction.Monthly && exec.Ok)
        {
            message =
                $"Ежемесячная передача включена: 1-е число в {schedule.Hour:D2}:{schedule.Minute:D2}.\n"
                + message;
        }

        return ToResult(exec, message, reniBusy);
    }

    private static bool MatchesSessionCheck(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (!t.Contains("сесси", StringComparison.Ordinal)
            && !t.Contains("session", StringComparison.Ordinal)
            && !t.Contains("checksession", StringComparison.Ordinal))
        {
            return false;
        }

        return t.Contains("reni", StringComparison.Ordinal)
               || t.Contains("рени", StringComparison.Ordinal)
               || t.Contains("водоканал", StringComparison.Ordinal)
               || t.Contains("вод", StringComparison.Ordinal);
    }

    private static ReniWaterLocalHandleResult ToResult(
        WpfLocalActionResult exec,
        string? displayText = null,
        bool reniBusy = false) =>
        new()
        {
            DisplayText = displayText ?? exec.UserMessage,
            ScreenshotPath = exec.ScreenshotPath,
            Ok = exec.Ok,
            ReniBusy = reniBusy,
        };
}

public sealed class ReniWaterLocalHandleResult
{
    public required string DisplayText { get; init; }

    public string? ScreenshotPath { get; init; }

    public bool Ok { get; init; }

    public bool ReniBusy { get; init; }
}
