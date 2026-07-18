using System.Collections.Concurrent;

namespace HermesWpfGuiController
{
    /// <summary>
    /// Статические методы этого класса вызываются напрямую из MQL5 через
    /// #import "HermesWpfGuiController.dll" — без DllExport/COM, т.к. MQL5 нативно
    /// поддерживает открытые статические методы .Net Framework сборок.
    /// Класс обычный (не static class) — как в документации MT5 (TestClass)
    /// и в MtGuiController. Имя != namespace/DLL, иначе MetaEditor путает
    /// namespace::func и Class::Method.
    /// </summary>
    public class GuiController
    {
        private static readonly ConcurrentDictionary<string, WpfWindowController> Controllers =
            new ConcurrentDictionary<string, WpfWindowController>();

        private static string Key(string assemblyPath, string windowName) => assemblyPath + "|" + windowName;

        public static bool ShowWindow(string assemblyPath, string windowName)
        {
            string key = Key(assemblyPath, windowName);

            if (Controllers.TryGetValue(key, out var existing) && !existing.IsDisposed)
                return true; // уже открыто

            var controller = new WpfWindowController();
            controller.Start(assemblyPath, windowName);
            Controllers[key] = controller;
            return true;
        }

        public static void HideWindow(string assemblyPath, string windowName)
        {
            string key = Key(assemblyPath, windowName);
            if (Controllers.TryRemove(key, out var controller))
                controller.Dispose();
        }

        public static bool IsWindowOpen(string assemblyPath, string windowName)
        {
            string key = Key(assemblyPath, windowName);
            return Controllers.TryGetValue(key, out var c) && !c.IsDisposed;
        }

        public static int EventsTotal(string assemblyPath, string windowName)
        {
            string key = Key(assemblyPath, windowName);
            return Controllers.TryGetValue(key, out var c) ? c.EventsTotal() : 0;
        }

        public static void GetEvent(string assemblyPath, string windowName, int index,
            ref string elName, ref int id, ref long lparam, ref double dparam, ref string sparam)
        {
            string key = Key(assemblyPath, windowName);
            if (!Controllers.TryGetValue(key, out var c)) return;

            if (c.TryGetEvent(index, out string en, out int i, out long lp, out double dp, out string sp))
            {
                elName = en; id = i; lparam = lp; dparam = dp; sparam = sp;
            }
        }

        public static void ClearEvents(string assemblyPath, string windowName)
        {
            string key = Key(assemblyPath, windowName);
            if (Controllers.TryGetValue(key, out var c))
                c.ClearEvents();
        }

        public static bool SendEvent(string assemblyPath, string windowName,
            string elementName, int id, long lparam, double dparam, string sparam)
        {
            string key = Key(assemblyPath, windowName);
            if (!Controllers.TryGetValue(key, out var c)) return false;
            return c.SendEvent(elementName, (GuiEventType)id, lparam, dparam, sparam);
        }

        /// <summary>Закрывает все окна — вызывать из OnDeinit на всякий случай.</summary>
        public static void ShutdownAll()
        {
            foreach (var kv in Controllers)
                kv.Value.Dispose();
            Controllers.Clear();
        }
    }
}
