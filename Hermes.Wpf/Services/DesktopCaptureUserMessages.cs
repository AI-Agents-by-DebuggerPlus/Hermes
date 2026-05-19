using Hermes.DesktopCapture.Models;

namespace Hermes.Wpf.Services;

public static class DesktopCaptureUserMessages
{
    public static string BriefAfterCapture(ScreenCaptureResult capture, string? userVisibleFromModel)
    {
        if (!string.IsNullOrWhiteSpace(userVisibleFromModel))
        {
            return userVisibleFromModel.Trim();
        }

        var windows = ScreenCaptureRegionFilter.SelectApplicationWindows(capture.Regions);
        var fg = capture.ForegroundWindowTitle ?? "—";
        return
            $"Снимок сохранён ({windows.Count} окон, активное: {fg}). "
            + "Контекст для действий на экране обновлён. "
            + "Напишите «опиши экран» для подробного отчёта или «переключись в …» для разметки окна.";
    }

    public static string BriefCaptureOnly(ScreenCaptureResult capture) =>
        BriefAfterCapture(capture, userVisibleFromModel: null);
}
