using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Builds structured memory drafts from Reni Water automation runs.</summary>
public static class ReniWaterExperienceBuilder
{
    private const string SiteUrl = "https://my.renivodokanal.od.ua/lickar/main/counter-pokaz";

    public static MemoryDraft BuildSubmitDraft(LocalExecutionRecord record, HermesSettings settings, string? vaultScreenshotRel)
    {
        var reni = record.ReniResult;
        var schedule = DescribeSchedule(settings);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Автоматизация Hermes.Wpf (built-in Reni Water)");
        sb.AppendLine();
        sb.AppendLine($"- Сайт: {SiteUrl}");
        sb.AppendLine("- Скрипт: `scripts/reni_water/run_submit.ps1` → Playwright (Chromium profile)");
        sb.AppendLine("- UI сайта: украинский — «Показник на початок місяця» → «Новий показник» → «Передати»");
        sb.AppendLine($"- Расписание: {schedule}");
        sb.AppendLine($"- Последний успешный месяц (settings): {settings.ReniWaterLastMonthlyRunKey ?? "—"}");
        sb.AppendLine($"- Источник запуска: {record.TriggerSource}");
        if (reni is not null)
        {
            sb.AppendLine($"- Exit code: {reni.ExitCode}, SUBMIT_ACCEPTED={reni.SubmitAccepted}, AUTH_REQUIRED={reni.AuthRequired}");
        }

        if (!string.IsNullOrWhiteSpace(vaultScreenshotRel))
        {
            sb.AppendLine($"- Скриншот в vault: `{vaultScreenshotRel}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Edge cases");
        sb.AppendLine("- `AUTH_REQUIRED` — один раз `run_submit.ps1 -login` или `RENI_LOGIN_*` в `reni_water.env`");
        sb.AppendLine("- `pending_ack.json` — после submit напишите «принял» или кнопку «Подтвердить»");
        sb.AppendLine("- Windows Task Scheduler: только по решению Hermes CLI (`schtasks` или wpf_local reni_water_schtasks_register)");

        return new MemoryDraft
        {
            Type = record.Success ? "procedural" : "episodic",
            Problem = string.IsNullOrWhiteSpace(record.UserTask)
                ? "Reni Water: передача показаний водоканала"
                : record.UserTask,
            Solution = record.AssistantSummary,
            Reusable = sb.ToString(),
            Tags =
            [
                "hermes", "local-automation", "utilities", "reni", "vodokanal", "reni_water", "procedural",
            ],
            Project = record.ProjectName ?? string.Empty,
            Importance = record.Success ? 5 : 4,
            TimestampUtc = DateTime.UtcNow,
        };
    }

    public static MemoryDraft BuildStatusDraft(HermesSettings settings, int successCount)
    {
        var body = BuildStatusText(settings, successCount);
        return new MemoryDraft
        {
            Type = "semantic",
            Problem = "Статус автоматизации Reni Water (водоканал)",
            Solution = body,
            Reusable = body,
            Tags = ["hermes", "utilities", "reni", "vodokanal", "status"],
            Importance = 3,
            TimestampUtc = DateTime.UtcNow,
        };
    }

    public static string BuildStatusText(HermesSettings settings, int successCount)
    {
        var pending = settings.ReniWaterPendingAckPath;
        var pendingExists = !string.IsNullOrWhiteSpace(pending) && File.Exists(pending);
        return
            $"Reni Water (Рeni vodokanal): встроенный навык Hermes.Wpf.\n"
            + $"Расписание: {DescribeSchedule(settings)}\n"
            + $"Последний месяц передачи: {settings.ReniWaterLastMonthlyRunKey ?? "не отмечен"}\n"
            + $"Успешных передач (learning counter): {successCount}\n"
            + $"Ожидает подтверждения (pending_ack): {(pendingExists ? "да" : "нет")}\n"
            + "Команды через Hermes CLI (не автономно в WPF): «Передай показания», «статус расписания».\n"
            + "Расписание: Hermes CLI → schtasks или wpf_local reni_water_schtasks_register.";
    }

    private static string DescribeSchedule(HermesSettings settings)
    {
        var kind = (settings.ReniWaterScheduleKind ?? string.Empty).Trim();
        if (kind.Length > 0)
        {
            return "устаревшее in-app (отключено) — настройте через Hermes CLI + Task Scheduler";
        }

        return "управляется Hermes CLI (Windows Task Scheduler; Hermes.Wpf не запускает задачи сам)";
    }

    private static string FormatWindow(HermesSettings settings) =>
        $"с {settings.ReniWaterMonthlyWindowStartDay}-го по {settings.ReniWaterMonthlyWindowEndDay}-е число";
}
