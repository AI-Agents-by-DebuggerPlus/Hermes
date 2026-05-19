using System.Text;

using Hermes.DesktopCapture.Models;



namespace Hermes.Wpf.Services;



public static class ScreenCaptureSummaryBuilder

{

    /// <summary>Terminal / no-vision fallback; chat uses <see cref="DesktopCaptureUserMessages"/> when vision runs.</summary>

    public static string BuildChatSummary(ScreenCaptureResult capture, bool visionAnalysisPending = false)

    {

        var windows = ScreenCaptureRegionFilter.SelectApplicationWindows(capture.Regions);

        var sb = new StringBuilder();

        sb.Append($"Скриншот: {capture.Monitor.Width}×{capture.Monitor.Height}, окон {windows.Count}.");

        if (!string.IsNullOrWhiteSpace(capture.ForegroundWindowTitle))

        {

            sb.Append($" Активное: {capture.ForegroundWindowTitle}.");

        }



        if (visionAnalysisPending)

        {

            sb.Append(" Анализ…");

        }



        return sb.ToString();

    }

}


