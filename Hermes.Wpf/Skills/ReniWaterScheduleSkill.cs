using System.Globalization;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Skills;

/// <summary>
/// In-app monthly water readings: once per calendar month, days 1–5 (default), catch-up on startup if missed.
/// </summary>
public sealed class ReniWaterScheduleSkill : IDisposable
{
    public const int DefaultWindowStartDay = 1;
    public const int DefaultWindowEndDay = 5;
    public const int DefaultHour = 9;
    public const int DefaultMinute = 0;

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly Func<Task> _runSubmitAsync;
    private readonly object _gate = new();

    private string _kind = string.Empty;
    private DateTime? _nextRunLocal;
    private int _windowStartDay = DefaultWindowStartDay;
    private int _windowEndDay = DefaultWindowEndDay;
    private int _hour = DefaultHour;
    private int _minute = DefaultMinute;
    private string? _lastMonthlyKey;

    public ReniWaterScheduleSkill(
        LogService log,
        Func<HermesSettings> settings,
        Func<Task> runSubmitAsync)
    {
        _log = log;
        _settings = settings;
        _runSubmitAsync = runSubmitAsync;
        LoadFromSettings();
    }

    public bool IsMonthlyEnabled
    {
        get
        {
            lock (_gate)
            {
                return _kind == "monthly";
            }
        }
    }

    public string DescribeSchedule()
    {
        lock (_gate)
        {
            return _kind switch
            {
                "once" when _nextRunLocal is { } t =>
                    $"Разовая передача: {t:dd.MM.yyyy HH:mm} (локальное время).",
                "monthly" =>
                    $"Ежемесячно: один раз с {_windowStartDay}-го по {_windowEndDay}-е число "
                    + $"(по умолчанию в {_hour:D2}:{_minute:D2}; при запуске Hermes.Wpf — догон, если пропустили).",
                _ => "Ежемесячная передача выключена.",
            };
        }
    }

    public void Apply(ReniWaterScheduleRequest request)
    {
        lock (_gate)
        {
            switch (request.Action)
            {
                case ReniWaterScheduleAction.Cancel:
                    _kind = string.Empty;
                    _nextRunLocal = null;
                    _log.LogInfo("[reni-water] schedule cleared");
                    break;
                case ReniWaterScheduleAction.Once:
                    _kind = "once";
                    _nextRunLocal = request.RunAtLocal;
                    _log.LogInfo($"[reni-water] schedule once at {_nextRunLocal:O}");
                    break;
                case ReniWaterScheduleAction.Monthly:
                    _kind = "monthly";
                    _windowStartDay = request.WindowStartDay;
                    _windowEndDay = request.WindowEndDay;
                    _hour = request.Hour;
                    _minute = request.Minute;
                    _nextRunLocal = null;
                    _log.LogInfo(
                        $"[reni-water] schedule monthly window={_windowStartDay}-{_windowEndDay} "
                        + $"time={_hour:D2}:{_minute:D2}");
                    break;
            }

            PersistLocked();
        }
    }

    /// <summary>After successful submit — one transfer per calendar month.</summary>
    public void MarkMonthCompleted()
    {
        lock (_gate)
        {
            _lastMonthlyKey = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            PersistLocked();
            _log.LogInfo($"[reni-water] month marked done: {_lastMonthlyKey}");
        }
    }

    public async Task RunStartupCatchUpAsync()
    {
        if (!TryTakeMonthlyFire(DateTime.Now, startupCatchUp: true))
        {
            return;
        }

        _log.LogInfo("[reni-water] startup catch-up: running monthly submit");
        await FireSubmitAsync().ConfigureAwait(false);
    }

    public async Task TickAsync()
    {
        if (TryTakeOnceFire())
        {
            _log.LogInfo("[reni-water] schedule fired (once)");
            await FireSubmitAsync().ConfigureAwait(false);
            return;
        }

        if (TryTakeMonthlyFire(DateTime.Now, startupCatchUp: false))
        {
            _log.LogInfo("[reni-water] schedule fired (monthly)");
            await FireSubmitAsync().ConfigureAwait(false);
        }
    }

    private async Task FireSubmitAsync()
    {
        try
        {
            await _runSubmitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError($"[reni-water] scheduled submit failed: {ex.Message}");
        }
    }

    private bool TryTakeOnceFire()
    {
        lock (_gate)
        {
            if (_kind != "once" || _nextRunLocal is not { } onceAt || DateTime.Now < onceAt)
            {
                return false;
            }

            _kind = string.Empty;
            _nextRunLocal = null;
            PersistLocked();
            return true;
        }
    }

    private bool TryTakeMonthlyFire(DateTime now, bool startupCatchUp)
    {
        lock (_gate)
        {
            if (_kind != "monthly" || !NeedsMonthlySubmitLocked(now))
            {
                return false;
            }

            if (!IsInMonthlyWindowLocked(now))
            {
                return false;
            }

            if (!startupCatchUp)
            {
                var runAt = new DateTime(now.Year, now.Month, now.Day, _hour, _minute, 0);
                if (now < runAt)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool NeedsMonthlySubmitLocked(DateTime now)
    {
        var monthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return !string.Equals(_lastMonthlyKey, monthKey, StringComparison.Ordinal);
    }

    private bool IsInMonthlyWindowLocked(DateTime now)
    {
        var day = now.Day;
        return day >= _windowStartDay && day <= _windowEndDay;
    }

    public void LoadFromSettings()
    {
        var s = _settings();
        lock (_gate)
        {
            _kind = (s.ReniWaterScheduleKind ?? string.Empty).Trim();
            var ws = s.ReniWaterMonthlyWindowStartDay > 0
                ? s.ReniWaterMonthlyWindowStartDay
                : s.ReniWaterMonthlyDay > 0 ? s.ReniWaterMonthlyDay : DefaultWindowStartDay;
            _windowStartDay = Math.Clamp(ws, 1, 28);
            _windowEndDay = Math.Clamp(
                s.ReniWaterMonthlyWindowEndDay > 0 ? s.ReniWaterMonthlyWindowEndDay : DefaultWindowEndDay,
                _windowStartDay,
                28);
            _hour = Math.Clamp(s.ReniWaterScheduleHour, 0, 23);
            _minute = Math.Clamp(s.ReniWaterScheduleMinute, 0, 59);
            _lastMonthlyKey = s.ReniWaterLastMonthlyRunKey;
            _nextRunLocal = null;
            if (!string.IsNullOrWhiteSpace(s.ReniWaterNextRunLocal)
                && DateTime.TryParse(s.ReniWaterNextRunLocal, null, DateTimeStyles.RoundtripKind, out var dt))
            {
                _nextRunLocal = dt.Kind == DateTimeKind.Unspecified ? dt : dt.ToLocalTime();
            }
        }
    }

    private void PersistLocked()
    {
        var s = _settings();
        s.ReniWaterScheduleKind = _kind;
        s.ReniWaterMonthlyWindowStartDay = _windowStartDay;
        s.ReniWaterMonthlyWindowEndDay = _windowEndDay;
        s.ReniWaterScheduleHour = _hour;
        s.ReniWaterScheduleMinute = _minute;
        s.ReniWaterLastMonthlyRunKey = _lastMonthlyKey;
        s.ReniWaterNextRunLocal = _nextRunLocal?.ToString("o");
    }

    public void Dispose()
    {
    }
}
