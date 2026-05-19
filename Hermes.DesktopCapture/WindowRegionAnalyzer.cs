using System.Drawing;
using System.Windows.Automation;
using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class WindowRegionAnalyzer
{
    private static readonly string[] CloseNames = ["Close", "Закрыть", "Закрити"];
    private static readonly string[] MinimizeNames = ["Minimize", "Свернуть", "Згорнути"];
    private static readonly string[] MaximizeNames = ["Maximize", "Развернуть", "Відновити", "Restore"];

    private sealed record WindowContext(
        string Prefix,
        IntPtr Hwnd,
        string Title,
        string ProcessName,
        string ApplicationDisplay,
        string WindowDisplay);

    /// <summary>All visible top-level windows on the monitor + taskbar.</summary>
    public static IReadOnlyList<ScreenRegion> AnalyzeMonitor(Rectangle monitorBounds)
    {
        var regions = new List<ScreenRegion>();
        var windows = NativeWindow.EnumerateVisibleWindows(monitorBounds);

        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var title = string.IsNullOrWhiteSpace(w.Title) ? $"Окно {i + 1}" : w.Title;
            var ctx = BuildContext($"w{i}", w.Hwnd, title, w.ProcessName);
            AnalyzeWindow(ctx, w.Bounds, regions);
        }

        if (NativeWindow.TryGetTaskbarBounds(out var taskbar) && taskbar.IntersectsWith(monitorBounds))
        {
            var local = Rectangle.Intersect(taskbar, monitorBounds);
            if (local.Width > 0 && local.Height > 0)
            {
                regions.Add(
                    CreateRegion(
                        null,
                        "taskbar",
                        ScreenRegionRole.TaskBar,
                        "Панель задач",
                        local,
                        applicationDisplay: string.Empty,
                        windowDisplay: string.Empty));
            }
        }

        return ScreenRegionCatalog.NumberAndFinalize(regions);
    }

    public static IReadOnlyList<ScreenRegion> AnalyzeForegroundWindow(Rectangle monitorBounds)
    {
        var hwnd = NativeWindow.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeWindow.GetWindowRect(hwnd, out var wr))
        {
            return [];
        }

        var windowRect = NativeWindow.ToDrawingRect(wr);
        if (!windowRect.IntersectsWith(monitorBounds))
        {
            return [];
        }

        var title = NativeWindow.GetWindowTitle(hwnd);
        var processName = WindowDisplayNames.GetProcessName(hwnd);
        var ctx = BuildContext(
            "w0",
            hwnd,
            string.IsNullOrWhiteSpace(title) ? "Активное окно" : title,
            processName);
        var regions = new List<ScreenRegion>();
        AnalyzeWindow(ctx, windowRect, regions);
        return ScreenRegionCatalog.NumberAndFinalize(regions);
    }

    private static WindowContext BuildContext(string prefix, IntPtr hwnd, string title, string processName)
    {
        var app = WindowDisplayNames.FormatApplication(hwnd, title, processName);
        var win = WindowDisplayNames.FormatWindow(hwnd, title, processName);
        return new WindowContext(prefix, hwnd, title, processName, app, win);
    }

    private static void AnalyzeWindow(WindowContext ctx, Rectangle windowRect, List<ScreenRegion> regions)
    {
        regions.Add(
            CreateRegion(
                ctx,
                $"{ctx.Prefix}_window",
                ScreenRegionRole.ApplicationWindow,
                ctx.Title,
                windowRect,
                ctx.ApplicationDisplay,
                ctx.WindowDisplay));

        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(ctx.Hwnd);
        }
        catch
        {
            AddTitleBarHeuristic(ctx, windowRect, regions);
            return;
        }

        if (root is null)
        {
            AddTitleBarHeuristic(ctx, windowRect, regions);
            return;
        }

        TryAddControlType(
            ctx,
            root,
            ControlType.TitleBar,
            ScreenRegionRole.TitleBar,
            "Панель заголовка",
            $"{ctx.Prefix}_titlebar",
            regions);
        TryAddControlType(ctx, root, ControlType.MenuBar, ScreenRegionRole.MenuBar, "Меню", $"{ctx.Prefix}_menu", regions);
        TryAddNamedButtons(ctx, root, regions);
        TryAddEditor(ctx, root, windowRect, regions);

        if (regions.All(r => r.Id != $"{ctx.Prefix}_titlebar"))
        {
            AddTitleBarHeuristic(ctx, windowRect, regions);
        }
    }

    private static void TryAddControlType(
        WindowContext ctx,
        AutomationElement root,
        ControlType controlType,
        ScreenRegionRole role,
        string label,
        string id,
        List<ScreenRegion> regions)
    {
        try
        {
            var element = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
            if (element is null)
            {
                return;
            }

            AddFromElement(ctx, element, role, label, id, regions);
        }
        catch
        {
            // UIA can fail on some HWNDs.
        }
    }

    private static void TryAddNamedButtons(WindowContext ctx, AutomationElement root, List<ScreenRegion> regions)
    {
        TryAddNamedButton(ctx, root, CloseNames, ScreenRegionRole.CloseButton, "Закрыть", $"{ctx.Prefix}_btn_close", regions);
        TryAddNamedButton(
            ctx,
            root,
            MinimizeNames,
            ScreenRegionRole.MinimizeButton,
            "Свернуть",
            $"{ctx.Prefix}_btn_minimize",
            regions);
        TryAddNamedButton(
            ctx,
            root,
            MaximizeNames,
            ScreenRegionRole.MaximizeButton,
            "Развернуть",
            $"{ctx.Prefix}_btn_maximize",
            regions);
    }

    private static void TryAddNamedButton(
        WindowContext ctx,
        AutomationElement root,
        string[] names,
        ScreenRegionRole role,
        string defaultLabel,
        string id,
        List<ScreenRegion> regions)
    {
        foreach (var name in names)
        {
            try
            {
                var element = root.FindFirst(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                        new PropertyCondition(AutomationElement.NameProperty, name)));
                if (element is null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(element.Current.Name) ? defaultLabel : element.Current.Name;
                AddFromElement(ctx, element, role, label, id, regions);
                return;
            }
            catch
            {
                // try next name
            }
        }
    }

    private static void TryAddEditor(
        WindowContext ctx,
        AutomationElement root,
        Rectangle windowRect,
        List<ScreenRegion> regions)
    {
        AutomationElement? best = null;
        var bestArea = 0.0;

        try
        {
            var types = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane));

            var nodes = root.FindAll(TreeScope.Descendants, types);
            foreach (AutomationElement node in nodes)
            {
                var rect = ToDrawingRect(node.Current.BoundingRectangle);
                if (rect.Width < 80 || rect.Height < 80)
                {
                    continue;
                }

                var area = rect.Width * (double)rect.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = node;
                }
            }
        }
        catch
        {
            return;
        }

        if (best is null || bestArea < windowRect.Width * windowRect.Height * 0.08)
        {
            return;
        }

        AddFromElement(ctx, best, ScreenRegionRole.Editor, "Область редактора", $"{ctx.Prefix}_editor", regions);
    }

    private static void AddTitleBarHeuristic(WindowContext ctx, Rectangle windowRect, List<ScreenRegion> regions)
    {
        if (regions.Any(r => r.Id == $"{ctx.Prefix}_titlebar" || r.Id == $"{ctx.Prefix}_titlebar_heuristic"))
        {
            return;
        }

        var height = Math.Clamp((int)(windowRect.Height * 0.08), 28, 64);
        var bar = new Rectangle(windowRect.Left, windowRect.Top, windowRect.Width, height);
        regions.Add(
            CreateRegion(
                ctx,
                $"{ctx.Prefix}_titlebar_heuristic",
                ScreenRegionRole.TitleBar,
                "Панель заголовка",
                bar,
                ctx.ApplicationDisplay,
                ctx.WindowDisplay));

        var btn = Math.Clamp(height - 6, 24, 40);
        var y = windowRect.Top + Math.Max(2, (height - btn) / 2);
        var x = windowRect.Right - btn - 8;
        regions.Add(
            CreateRegion(
                ctx,
                $"{ctx.Prefix}_btn_close_h",
                ScreenRegionRole.CloseButton,
                "Закрыть",
                new Rectangle(x, y, btn, btn),
                ctx.ApplicationDisplay,
                ctx.WindowDisplay));
        x -= btn + 6;
        regions.Add(
            CreateRegion(
                ctx,
                $"{ctx.Prefix}_btn_maximize_h",
                ScreenRegionRole.MaximizeButton,
                "Развернуть",
                new Rectangle(x, y, btn, btn),
                ctx.ApplicationDisplay,
                ctx.WindowDisplay));
        x -= btn + 6;
        regions.Add(
            CreateRegion(
                ctx,
                $"{ctx.Prefix}_btn_minimize_h",
                ScreenRegionRole.MinimizeButton,
                "Свернуть",
                new Rectangle(x, y, btn, btn),
                ctx.ApplicationDisplay,
                ctx.WindowDisplay));
    }

    private static void AddFromElement(
        WindowContext ctx,
        AutomationElement element,
        ScreenRegionRole role,
        string label,
        string id,
        List<ScreenRegion> regions)
    {
        var rect = ToDrawingRect(element.Current.BoundingRectangle);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        if (regions.Any(r => r.Id == id))
        {
            return;
        }

        regions.Add(CreateRegion(ctx, id, role, label, rect, ctx.ApplicationDisplay, ctx.WindowDisplay));
    }

    private static ScreenRegion CreateRegion(
        WindowContext? ctx,
        string id,
        ScreenRegionRole role,
        string label,
        Rectangle rect,
        string applicationDisplay,
        string windowDisplay) =>
        new()
        {
            Id = id,
            Role = role,
            Label = label,
            WindowPrefix = ctx?.Prefix,
            OwnerWindowTitle = ctx?.Title,
            OwnerProcessName = ctx?.ProcessName,
            OwnerApplicationDisplay = string.IsNullOrWhiteSpace(applicationDisplay) ? null : applicationDisplay,
            OwnerWindowDisplay = string.IsNullOrWhiteSpace(windowDisplay) ? null : windowDisplay,
            DisplayName = WindowDisplayNames.FormatRegionDisplayName(
                role,
                label,
                applicationDisplay,
                windowDisplay,
                ctx?.Title ?? label),
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
        };

    private static Rectangle ToDrawingRect(System.Windows.Rect rect) =>
        rect.IsEmpty
            ? Rectangle.Empty
            : Rectangle.FromLTRB(
                (int)Math.Floor(rect.Left),
                (int)Math.Floor(rect.Top),
                (int)Math.Ceiling(rect.Right),
                (int)Math.Ceiling(rect.Bottom));
}
