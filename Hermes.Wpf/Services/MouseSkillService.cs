using Hermes.DesktopInteraction;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Opt-in wrappers for desktop cursor control exposed as a Hermes.Wpf skill.</summary>
public sealed class MouseSkillService
{
    private readonly HermesSettings _settings;
    private readonly LogService _log;

    public MouseSkillService(HermesSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    public bool IsEnabled => _settings.DesktopMouseSkillEnabled;

    /// <summary>Moves cursor 48 px right then restores after ~320 ms (smoke test).</summary>
    public void RunSmokeShift()
    {
        if (!_settings.DesktopMouseSkillEnabled)
        {
            _log.LogWarn("[mouse-skill] Включите опцию «Разрешить управление курсором» в Settings.");
            return;
        }

        if (!DesktopMouse.TryGetCursorPos(out var p))
        {
            _log.LogWarn("[mouse-skill] GetCursorPos failed.");
            return;
        }

        var ox = p.X;
        var oy = p.Y;
        if (!DesktopMouse.MoveBy(48, 0))
        {
            _log.LogWarn("[mouse-skill] MoveBy failed.");
            return;
        }

        _log.LogInfo("[mouse-skill] Smoke: курсор сдвинут вправо, возврат через ~320 ms.");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(320).ConfigureAwait(false);
                DesktopMouse.MoveTo(ox, oy);
                _log.LogInfo("[mouse-skill] Smoke: позиция восстановлена.");
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[mouse-skill] Smoke restore: {ex.Message}");
            }
        });
    }

    public bool MoveTo(int x, int y)
    {
        if (!_settings.DesktopMouseSkillEnabled)
        {
            _log.LogWarn("[mouse-skill] MoveTo: навык выключен в Settings.");
            return false;
        }

        var ok = DesktopMouse.MoveTo(x, y);
        if (ok)
        {
            _log.LogInfo($"[mouse-skill] MoveTo ({x},{y}).");
        }

        return ok;
    }

    public bool MoveBy(int dx, int dy)
    {
        if (!_settings.DesktopMouseSkillEnabled)
        {
            _log.LogWarn("[mouse-skill] MoveBy: навык выключен в Settings.");
            return false;
        }

        var ok = DesktopMouse.MoveBy(dx, dy);
        if (ok)
        {
            _log.LogInfo($"[mouse-skill] MoveBy ({dx},{dy}).");
        }

        return ok;
    }

    public bool LeftClick(int repeat = 1)
    {
        if (!_settings.DesktopMouseSkillEnabled)
        {
            _log.LogWarn("[mouse-skill] LeftClick: навык выключен в Settings.");
            return false;
        }

        DesktopMouse.LeftClick(Math.Clamp(repeat, 1, 5));
        _log.LogInfo($"[mouse-skill] LeftClick x{repeat}.");
        return true;
    }

    public bool RightClick()
    {
        if (!_settings.DesktopMouseSkillEnabled)
        {
            _log.LogWarn("[mouse-skill] RightClick: навык выключен в Settings.");
            return false;
        }

        DesktopMouse.RightClick();
        _log.LogInfo("[mouse-skill] RightClick.");
        return true;
    }
}
