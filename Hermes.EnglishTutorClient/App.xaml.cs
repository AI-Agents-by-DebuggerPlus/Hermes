using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Hermes.EnglishTutorClient.Services;

namespace Hermes.EnglishTutorClient;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        // Logs live under Hermes.EnglishTutorClient/logs; wipe on every restart.
        AppLog.LogFolder = AppLog.ResolveDefaultLogFolder();
        AppLog.Clear();

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash("UI", args.Exception);
            MessageBox.Show(args.Exception.Message, "Hermes English Tutor Client",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrash("Domain", ex);
        };

        if (e.Args != null && e.Args.Any(a =>
                string.Equals(a, "--stt-selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = e.Args
                .SkipWhile(a => !string.Equals(a, "--stt-selftest", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .ToArray();
            var hint = rest.Length > 0 ? string.Join(" ", rest) : null;
            var code = SttSelfTest.Run(hint);
            Shutdown(code);
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static void WriteCrash(string kind, Exception ex)
    {
        try
        {
            var dir = AppLog.LogFolder;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                DateTime.Now.ToString("s") + " [" + kind + "] " + ex + Environment.NewLine);
        }
        catch { /* ignore */ }
    }
}
