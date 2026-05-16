using System.Globalization;
using System.Text.RegularExpressions;
using Hermes.Wpf.Skills;

namespace Hermes.Wpf.Services;

public enum ReniWaterScheduleAction
{
    None,
    Once,
    Monthly,
    Cancel,
    Status,
}

public sealed record ReniWaterScheduleRequest(
    ReniWaterScheduleAction Action,
    DateTime? RunAtLocal,
    int WindowStartDay,
    int WindowEndDay,
    int Hour,
    int Minute)
{
    public static ReniWaterScheduleRequest DefaultMonthly() =>
        new(
            ReniWaterScheduleAction.Monthly,
            null,
            ReniWaterScheduleSkill.DefaultWindowStartDay,
            ReniWaterScheduleSkill.DefaultWindowEndDay,
            ReniWaterScheduleSkill.DefaultHour,
            ReniWaterScheduleSkill.DefaultMinute);
}

/// <summary>Parse Russian chat phrases for in-app water-meter scheduling (no Windows Task Scheduler).</summary>
public static partial class ReniWaterScheduleParser
{
    private static readonly Dictionary<string, int> HourWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["один"] = 1,
        ["два"] = 2,
        ["две"] = 2,
        ["три"] = 3,
        ["четыре"] = 4,
        ["четырёх"] = 4,
        ["пять"] = 5,
        ["шесть"] = 6,
        ["семь"] = 7,
        ["восемь"] = 8,
        ["девять"] = 9,
        ["десять"] = 10,
        ["одиннадцать"] = 11,
        ["двенадцать"] = 12,
    };

    public static bool TryParse(string message, out ReniWaterScheduleRequest request)
    {
        request = new ReniWaterScheduleRequest(ReniWaterScheduleAction.None, null, 1, 5, 9, 0);

        var t = (message ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0 || !HasWaterContext(t))
        {
            return false;
        }

        if (IsCancel(t))
        {
            request = new ReniWaterScheduleRequest(ReniWaterScheduleAction.Cancel, null, 1, 5, 9, 0);
            return true;
        }

        if (IsStatus(t))
        {
            request = new ReniWaterScheduleRequest(ReniWaterScheduleAction.Status, null, 1, 5, 9, 0);
            return true;
        }

        if (TryParseMonthly(t, out var monthly))
        {
            request = monthly;
            return true;
        }

        if (TryParseOnce(t, out var once))
        {
            request = once;
            return true;
        }

        return false;
    }

    public static bool IsSchedulePhrase(string message) =>
        TryParse(message, out var r) && r.Action != ReniWaterScheduleAction.None;

    private static bool HasWaterContext(string t) =>
        t.Contains("показан", StringComparison.Ordinal)
        || t.Contains("водоканал", StringComparison.Ordinal)
        || t.Contains("вод", StringComparison.Ordinal)
        || t.Contains("рени", StringComparison.Ordinal);

    private static bool IsCancel(string t) =>
        (t.Contains("отмен", StringComparison.Ordinal) || t.Contains("останов", StringComparison.Ordinal)
                                                       || t.Contains("выключ", StringComparison.Ordinal))
        && (t.Contains("расписан", StringComparison.Ordinal) || t.Contains("показан", StringComparison.Ordinal)
            || t.Contains("каждый месяц", StringComparison.Ordinal) || t.Contains("ежемесяч", StringComparison.Ordinal));

    private static bool IsStatus(string t) =>
        t.Contains("расписан", StringComparison.Ordinal)
        && (t.Contains("когда", StringComparison.Ordinal) || t.Contains("статус", StringComparison.Ordinal));

    private static bool TryParseMonthly(string t, out ReniWaterScheduleRequest request)
    {
        request = ReniWaterScheduleRequest.DefaultMonthly();

        var monthlyCue =
            t.Contains("каждый месяц", StringComparison.Ordinal)
            || t.Contains("каждого месяца", StringComparison.Ordinal)
            || t.Contains("ежемесяч", StringComparison.Ordinal)
            || (t.Contains("передав", StringComparison.Ordinal) && t.Contains("месяц", StringComparison.Ordinal))
            || (t.Contains("начн", StringComparison.Ordinal) && t.Contains("передав", StringComparison.Ordinal));

        if (!monthlyCue)
        {
            return false;
        }

        var windowStart = ReniWaterScheduleSkill.DefaultWindowStartDay;
        var windowEnd = ReniWaterScheduleSkill.DefaultWindowEndDay;

        var range = WindowRangeRegex().Match(t);
        if (range.Success)
        {
            if (int.TryParse(range.Groups[1].Value, out var a))
            {
                windowStart = Math.Clamp(a, 1, 28);
            }

            if (int.TryParse(range.Groups[2].Value, out var b))
            {
                windowEnd = Math.Clamp(b, windowStart, 28);
            }
        }
        else
        {
            var dayMatch = MonthlyDayRegex().Match(t);
            if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var d))
            {
                windowStart = Math.Clamp(d, 1, 28);
            }
        }

        var (hour, minute) = ParseTimeFromText(t, defaultHour: ReniWaterScheduleSkill.DefaultHour);
        request = new ReniWaterScheduleRequest(
            ReniWaterScheduleAction.Monthly,
            null,
            windowStart,
            windowEnd,
            hour,
            minute);
        return true;
    }

    private static bool TryParseOnce(string t, out ReniWaterScheduleRequest request)
    {
        request = new ReniWaterScheduleRequest(ReniWaterScheduleAction.None, null, 1, 5, 9, 0);

        if (t.Contains("каждый месяц", StringComparison.Ordinal)
            || t.Contains("ежемесяч", StringComparison.Ordinal))
        {
            return false;
        }

        if (!t.Contains("переда", StringComparison.Ordinal) && !t.Contains("показан", StringComparison.Ordinal))
        {
            return false;
        }

        if (!t.Contains(" в ", StringComparison.Ordinal) && !AtTimeRegex().IsMatch(t))
        {
            return false;
        }

        var (hour, minute) = ParseTimeFromText(t, defaultHour: -1);
        if (hour < 0)
        {
            return false;
        }

        var now = DateTime.Now;
        var runAt = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
        if (runAt <= now)
        {
            runAt = runAt.AddDays(1);
        }

        request = new ReniWaterScheduleRequest(ReniWaterScheduleAction.Once, runAt, 1, 5, hour, minute);
        return true;
    }

    private static (int Hour, int Minute) ParseTimeFromText(string t, int defaultHour)
    {
        var clock = ClockRegex().Match(t);
        if (clock.Success)
        {
            var h = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
            var m = clock.Groups[2].Success
                ? int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture)
                : 0;
            return (Math.Clamp(h, 0, 23), Math.Clamp(m, 0, 59));
        }

        var atHour = AtHourRegex().Match(t);
        if (atHour.Success)
        {
            var token = atHour.Groups[1].Value;
            if (int.TryParse(token, out var hNum))
            {
                return (ResolveColloquialHour(hNum, t), 0);
            }

            if (HourWords.TryGetValue(token, out var hWord))
            {
                return (ResolveColloquialHour(hWord, t), 0);
            }
        }

        return defaultHour >= 0 ? (defaultHour, 0) : (-1, 0);
    }

    private static int ResolveColloquialHour(int hour12, string t)
    {
        if (t.Contains("утр", StringComparison.Ordinal))
        {
            return Math.Clamp(hour12, 0, 23);
        }

        if (t.Contains("вечер", StringComparison.Ordinal) || t.Contains("дня", StringComparison.Ordinal)
            || t.Contains("пополудн", StringComparison.Ordinal))
        {
            return hour12 == 12 ? 12 : hour12 + 12;
        }

        if (hour12 >= 5 && hour12 <= 11)
        {
            return hour12 + 12;
        }

        if (hour12 == 12)
        {
            return 12;
        }

        return hour12;
    }

    [GeneratedRegex(@"\b(\d{1,2})[-\s]*(?:го|ое|ого)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthlyDayRegex();

    [GeneratedRegex(
        @"с\s*(\d{1,2})[-\s]*(?:го|ое|ого)?\s*по\s*(\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowRangeRegex();

    [GeneratedRegex(@"\bв\s*(\d{1,2})\s*[:.]\s*(\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockRegex();

    [GeneratedRegex(@"\bв\s*(\d{1,2}|[а-яё]+)\s*час", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AtHourRegex();

    [GeneratedRegex(@"\bв\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AtTimeRegex();
}
