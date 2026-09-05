using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Detects Hermes scheduled work that should have run while the PC / Hermes.Wpf was off
/// (Reni Water monthly window, overdue once-schtasks).
/// </summary>
public sealed class MissedScheduledTaskService
{
    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public MissedScheduledTaskService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public IReadOnlyList<MissedScheduledTaskInfo> DetectMissed(DateTime? nowLocal = null)
    {
        var now = nowLocal ?? DateTime.Now;
        var list = new List<MissedScheduledTaskInfo>();

        TryAddReniMonthly(list, now);
        TryAddReniOnceSchTask(list, now);

        if (list.Count > 0)
        {
            _log.LogInfo($"[missed-tasks] detected {list.Count}: {string.Join(", ", list.Select(t => t.Id))}");
        }

        return list;
    }

    private void TryAddReniMonthly(List<MissedScheduledTaskInfo> list, DateTime now)
    {
        var s = _settings();
        var day = Math.Clamp(
            s.ReniWaterMonthlyWindowStartDay > 0 ? s.ReniWaterMonthlyWindowStartDay : s.ReniWaterMonthlyDay,
            1,
            28);
        var hour = Math.Clamp(s.ReniWaterScheduleHour, 0, 23);
        var minute = Math.Clamp(s.ReniWaterScheduleMinute, 0, 59);
        var endDay = Math.Clamp(s.ReniWaterMonthlyWindowEndDay, day, 28);

        var expected = new DateTime(now.Year, now.Month, day, hour, minute, 0, DateTimeKind.Local);
        if (now < expected)
        {
            return;
        }

        var monthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (string.Equals(s.ReniWaterLastMonthlyRunKey, monthKey, StringComparison.Ordinal))
        {
            return;
        }

        var sch = QuerySchTask(ReniWaterSchTasksService.MonthlyTaskName);
        var (cause, blame, fix) = DiagnoseMonthlyMiss(sch, expected, monthKey, endDay, now);

        list.Add(new MissedScheduledTaskInfo
        {
            Id = $"reni-water-monthly-{monthKey}",
            Title = "Показания воды — пропущена передача",
            Detail = FormatMissDetail(
                when: $"{expected:dd.MM.yyyy HH:mm} (окно до {endDay:00}.{now:MM.yyyy})",
                fact: $"Успешная передача за {monthKey} не зафиксирована (ReniWaterLastMonthlyRunKey пуст или старый).",
                cause: cause,
                blame: blame,
                fix: fix),
            ExpectedAtLocal = expected,
            Kind = MissedTaskKind.ReniWaterMonthly,
            SchTaskName = ReniWaterSchTasksService.MonthlyTaskName,
        });
    }

    private void TryAddReniOnceSchTask(List<MissedScheduledTaskInfo> list, DateTime now)
    {
        var sch = QuerySchTask(ReniWaterSchTasksService.OnceTaskName);
        if (!sch.Exists)
        {
            return;
        }

        if (sch.NextRun is { } next && next > now.AddMinutes(-1))
        {
            return;
        }

        if (sch.LastRun is { } last && last.Year > 2000 && sch.LastResult == 0)
        {
            return;
        }

        var expected = sch.NextRun ?? sch.LastRun ?? now;
        var (cause, blame, fix) = DiagnoseOnceMiss(sch, expected);

        list.Add(new MissedScheduledTaskInfo
        {
            Id = $"reni-water-once-{expected:yyyyMMddHHmm}",
            Title = "Показания воды — разовая задача не выполнена",
            Detail = FormatMissDetail(
                when: $"{expected:dd.MM.yyyy HH:mm}",
                fact: $"Задача {ReniWaterSchTasksService.OnceTaskName}: last={sch.LastRunDisplay}, result={sch.LastResult?.ToString() ?? "—"}.",
                cause: cause,
                blame: blame,
                fix: fix),
            ExpectedAtLocal = expected,
            Kind = MissedTaskKind.ReniWaterOnce,
            SchTaskName = ReniWaterSchTasksService.OnceTaskName,
        });
    }

    private static (string Cause, string Blame, string Fix) DiagnoseMonthlyMiss(
        (bool Exists, DateTime? LastRun, DateTime? NextRun, int? LastResult, string LastRunDisplay) sch,
        DateTime expected,
        string monthKey,
        int endDay,
        DateTime now)
    {
        if (!sch.Exists)
        {
            return (
                $"В Windows Task Scheduler нет задачи «{ReniWaterSchTasksService.MonthlyTaskName}». "
                + "Без неё 1-е число в 09:00 ничего не запускается — ни при включённом ПК, ни после простоя.",
                "Косяк агента Utilities / настройки: месячная задача не была зарегистрирована "
                + "(агент мог «пообещать» расписание в чате, но не вызвать schtasks / wpf_local reni_water_schtasks_register).",
                "1) Сейчас: нажмите Run Now — передача пойдёт локально (Playwright).\n"
                + "2) Чтобы не повторилось: в чате Utilities напишите «зарегистрируй ежемесячную передачу показаний» "
                + "и дождитесь успешного schtasks; либо от администратора:\n"
                + "   cd D:\\Programming\\AI_Agents\\Hermes\\scripts\\reni_water\n"
                + "   .\\register_scheduled_tasks.ps1\n"
                + "3) Проверка: schtasks /Query /TN Hermes_ReniWater_MonthlySubmit");
        }

        var ranThisMonth = sch.LastRun is { } lr
                           && lr >= expected
                           && lr.Year == now.Year
                           && lr.Month == now.Month;

        if (!ranThisMonth)
        {
            var never = sch.LastRun is null || sch.LastRun.Value.Year < 2000;
            return (
                never
                    ? $"Задача зарегистрирована, но ни разу не запускалась (Last Run: {sch.LastRunDisplay}). "
                      + $"Частая причина — ПК был выключен в {expected:dd.MM HH:mm}, а у задачи нет догона «при следующем включении»."
                    : $"Задача зарегистрирована, но в {monthKey} не отработала вовремя (Last Run: {sch.LastRunDisplay}, код {sch.LastResult}). "
                      + "Вероятно ПК/сеанс был выключен или планировщик пропустил старт.",
                never
                    ? "Скорее ПК / питание / сон в момент планового запуска — не ошибка чата. "
                      + "Но агент мог не включить «запускать при первой возможности после пропуска»."
                    : "Скорее ПК был выключен или задача завершилась с ошибкой (см. Last Result).",
                "1) Сейчас: Run Now.\n"
                + "2) В Task Scheduler → Hermes_ReniWater_MonthlySubmit → Properties → Settings:\n"
                + "   включите «Run task as soon as possible after a scheduled start is missed».\n"
                + $"3) ПК желательно включён 1-го числа около 09:00 (окно до {endDay:00}.{now:MM}).");
        }

        if (sch.LastResult is not null and not 0)
        {
            return (
                $"Планировщик запускал задачу ({sch.LastRunDisplay}), но скрипт завершился с кодом {sch.LastResult} "
                + "(сессия сайта, Playwright, пароль).",
                "Сбой автоматизации / сессии Reni Water, не «просто выключенный ПК».",
                "1) Run Now после проверки сессии (чат: «проверь сессию Reni Water» или run_submit.ps1 -CheckSession).\n"
                + "2) При необходимости: run_submit.ps1 -login и «Запам'ятати мене».\n"
                + "3) Логи/скриншоты: d:\\Documents\\Utilities\\water\\");
        }

        return (
            $"Планировщик, похоже, уже запускался ({sch.LastRunDisplay}), но Hermes.Wpf не отметил успех за {monthKey}. "
            + "Передача могла не пройти до конца или успех не записали в settings.",
            "Разрыв между скриптом и отметкой в Hermes.Wpf (агент/локальный handler).",
            "1) Run Now — повторная передача и запись месяца в settings.\n"
            + "2) После успеха в settings должен появиться ReniWaterLastMonthlyRunKey=" + monthKey + ".");
    }

    private static (string Cause, string Blame, string Fix) DiagnoseOnceMiss(
        (bool Exists, DateTime? LastRun, DateTime? NextRun, int? LastResult, string LastRunDisplay) sch,
        DateTime expected)
    {
        if (sch.LastResult is not null and not 0)
        {
            return (
                $"Разовая задача стартовала, но упала (result={sch.LastResult}, last={sch.LastRunDisplay}).",
                "Сбой скрипта / сессии, либо агент создал задачу с неверным временем/правами.",
                "Run Now. Затем проверьте сессию Reni Water и при необходимости пересоздайте разовую задачу в чате Utilities.");
        }

        return (
            $"Разовая задача «{ReniWaterSchTasksService.OnceTaskName}» не выполнилась к {expected:dd.MM.yyyy HH:mm} "
            + $"(last={sch.LastRunDisplay}). Часто ПК был выключен в этот момент.",
            "Если агент только «написал в чат, что запланировал», без реального schtasks — это косяк агента. "
            + "Если задача в планировщике есть, а ПК был off — вина среды, не чата.",
            "1) Run Now.\n"
            + "2) Для будущих разовых: в Utilities явно «запланируй передачу на ДАТА ВРЕМЯ» и проверьте "
            + "schtasks /Query /TN Hermes_ReniWater_OnceSubmit.");
    }

    private static string FormatMissDetail(
        string when,
        string fact,
        string cause,
        string blame,
        string fix)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Когда должно было сработать: {when}");
        sb.AppendLine();
        sb.AppendLine($"Факт: {fact}");
        sb.AppendLine();
        sb.AppendLine($"Причина: {cause}");
        sb.AppendLine();
        sb.AppendLine($"Чья вина: {blame}");
        sb.AppendLine();
        sb.Append($"Как исправить:\n{fix}");
        return sb.ToString();
    }

    private static (bool Exists, DateTime? LastRun, DateTime? NextRun, int? LastResult, string LastRunDisplay)
        QuerySchTask(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/Query /TN \"{taskName}\" /FO LIST /V",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + "\n" + p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            if (p.ExitCode != 0
                || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || output.Contains("не найден", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, null, null, "—");
            }

            var next = ParseSchDate(ExtractLine(output, "Next Run Time") ?? ExtractLine(output, "Следующий запуск"));
            var last = ParseSchDate(ExtractLine(output, "Last Run Time") ?? ExtractLine(output, "Последний запуск"));
            var resultRaw = ExtractLine(output, "Last Result") ?? ExtractLine(output, "Последний результат");
            int? result = null;
            if (int.TryParse(resultRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                result = r;
            }

            return (true, last, next, result, last?.ToString("dd.MM.yyyy HH:mm") ?? "никогда");
        }
        catch
        {
            return (false, null, null, null, "—");
        }
    }

    private static string? ExtractLine(string output, string keyPrefix)
    {
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var idx = t.IndexOf(':');
                return idx >= 0 ? t[(idx + 1)..].Trim() : t;
            }
        }

        return null;
    }

    private static DateTime? ParseSchDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Contains("N/A", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("никогда", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            return dt.Year < 2000 ? null : dt;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
        {
            return dt.Year < 2000 ? null : dt;
        }

        var m = Regex.Match(raw, @"(\d{1,2})[./](\d{1,2})[./](\d{4})\s+(\d{1,2}):(\d{2})");
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var d)
            && int.TryParse(m.Groups[2].Value, out var mo)
            && int.TryParse(m.Groups[3].Value, out var y)
            && int.TryParse(m.Groups[4].Value, out var h)
            && int.TryParse(m.Groups[5].Value, out var mi))
        {
            try
            {
                if (d > 12)
                {
                    return new DateTime(y, mo, d, h, mi, 0);
                }

                if (mo > 12)
                {
                    return new DateTime(y, d, mo, h, mi, 0);
                }

                return new DateTime(y, mo, d, h, mi, 0);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
