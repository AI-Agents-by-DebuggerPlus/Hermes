using System.Text;

using Hermes.DesktopCapture.Models;



namespace Hermes.Wpf.Services;



public static class DesktopVisionPromptBuilder

{

    public static string BuildUserRequest(

        ScreenCaptureResult capture,

        string wslAnnotatedImagePath,

        string wslPlainImagePath,

        string wslMetadataPath,

        DesktopVisionIntent intent,

        string? userRequest,

        string? focusWindowTarget = null)

    {

        var windows = ScreenCaptureRegionFilter.SelectApplicationWindows(capture.Regions);

        var sb = new StringBuilder();



        sb.AppendLine("Захват рабочего стола Windows (Hermes.Wpf). Вызови **vision_analyze** для:");

        sb.AppendLine($"  regions: {wslAnnotatedImagePath}");

        sb.AppendLine($"  plain: {wslPlainImagePath}");

        sb.AppendLine($"  JSON: {wslMetadataPath}");

        sb.AppendLine(

            $"Монитор {capture.Monitor.Width}×{capture.Monitor.Height}, захват {capture.CapturedAt:O}, foreground: {capture.ForegroundWindowTitle ?? "—"}.");

        sb.AppendLine();

        sb.AppendLine("Окна (номер на regions.png):");

        foreach (var w in windows)

        {

            sb.AppendLine($"{w.Index}. {ScreenCaptureRegionFilter.FormatWindowSummaryName(w)} (prefix {w.WindowPrefix})");

        }



        sb.AppendLine();

        sb.Append(MarkerInstructions());



        switch (intent)

        {

            case DesktopVisionIntent.FocusWindow:

                AppendFocusWindowTask(sb, capture, focusWindowTarget);

                break;

            case DesktopVisionIntent.DescribeScreen:

                AppendDescribeScreenTask(sb);

                break;

            default:

                AppendInternalCaptureTask(sb);

                break;

        }



        if (!string.IsNullOrWhiteSpace(userRequest)

            && !DesktopScreenCaptureTriggers.Matches(userRequest))

        {

            sb.AppendLine();

            sb.AppendLine($"Сообщение пользователя: {userRequest.Trim()}");

        }



        return sb.ToString().TrimEnd();

    }



    public static string BuildDescribeFromCacheRequest(DesktopScreenContextSnapshot snap, string wslAnnotated, string wslJson)

    {

        var sb = new StringBuilder();

        sb.AppendLine("Пользователь просит подробное описание последнего захвата экрана (без нового снимка).");

        sb.AppendLine($"regions: {wslAnnotated}");

        sb.AppendLine($"JSON: {wslJson}");

        sb.AppendLine();

        sb.AppendLine("Ранее сохранённый контекст:");

        sb.AppendLine(snap.InternalContext);

        sb.AppendLine();

        sb.Append(MarkerInstructions());

        sb.AppendLine();

        sb.AppendLine(

            "HERMES_DESKTOP_CTX_BEGIN — кратко обнови контекст при необходимости.\n"

            + "HERMES_DESKTOP_USER_BEGIN — полный структурированный отчёт для пользователя (это исключение: можно подробно).");

        return sb.ToString().TrimEnd();

    }



    private static string MarkerInstructions() =>

        "Формат ответа (обязательно, без тройных бэктиков вокруг маркеров):\n"

        + "HERMES_DESKTOP_CTX_BEGIN\n"

        + "…полный технический контекст для следующих команд агента: окна, номера регионов, координаты, что кликать…\n"

        + "HERMES_DESKTOP_CTX_END\n"

        + "HERMES_DESKTOP_USER_BEGIN\n"

        + "…текст для чата пользователя…\n"

        + "HERMES_DESKTOP_USER_END";



    private static void AppendInternalCaptureTask(StringBuilder sb)

    {

        sb.AppendLine();

        sb.AppendLine(

            "Задача (InternalCapture): в HERMES_DESKTOP_CTX — полный разбор для автоматизации (окна, UI-элементы, номера регионов, координаты). "

            + "В HERMES_DESKTOP_USER — не более 3 коротких предложений: снимок готов, сколько окон, активное окно. "

            + "Без длинного отчёта, без списков приложений в USER-блоке. "

            + "Подскажи, что «опиши экран» даст подробности.");

    }



    private static void AppendDescribeScreenTask(StringBuilder sb)

    {

        sb.AppendLine();

        sb.AppendLine(

            "Задача (DescribeScreen): в HERMES_DESKTOP_CTX — структурированный технический разбор. "

            + "В HERMES_DESKTOP_USER — полный понятный отчёт для пользователя (можно подробно).");

    }



    private static void AppendFocusWindowTask(StringBuilder sb, ScreenCaptureResult capture, string? focusWindowTarget)

    {

        var target = (focusWindowTarget ?? string.Empty).Trim();

        sb.AppendLine();

        sb.AppendLine($"Задача (FocusWindow): целевое окно — «{target}».");



        var appWindow = ScreenCaptureRegionFilter.TryFindApplicationWindow(capture.Regions, target);

        if (appWindow is null)

        {

            sb.AppendLine("Окно не найдено в JSON — укажи в USER, что совпадений нет, перечисли ближайшие окна из списка.");

            return;

        }



        sb.AppendLine(

            $"Сопоставлено: {ScreenCaptureRegionFilter.FormatWindowSummaryName(appWindow)} (prefix {appWindow.WindowPrefix}, регион №{appWindow.Index}).");

        sb.AppendLine("Регионы этого окна (для кликов / Hermes.MouseBridge, координаты экрана):");



        foreach (var r in ScreenCaptureRegionFilter.SelectRegionsForWindow(capture.Regions, appWindow))

        {

            sb.AppendLine(ScreenCaptureRegionFilter.FormatRegionLine(r));

        }



        sb.AppendLine();

        sb.AppendLine(

            "В HERMES_DESKTOP_CTX: как активировать окно (клик по titlebar №…), все интерактивные элементы с номерами и центрами. "

            + "В HERMES_DESKTOP_USER — кратко (до 4 предложений): какое окно, ключевые элементы для взаимодействия, без общего обзора всего стола.");

    }

}


