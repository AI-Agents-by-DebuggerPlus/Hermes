using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hermes.EnglishLearning.Services;

namespace Hermes.EnglishLearning;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch crashes before/while MainWindow loads and always leave a log on disk.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            ServicePointManager.Expect100Continue = false;
        }
        catch (Exception ex)
        {
            // logged after BeginSession
            _tlsBootError = ex.Message;
        }

        AppLog.BeginSession();
        AppLog.Info("BaseDir: " + AppDomain.CurrentDomain.BaseDirectory);
        AppLog.Info("Log file: " + AppLog.CurrentLogPath);
        try
        {
            AppLog.Info("SecurityProtocol=" + ServicePointManager.SecurityProtocol);
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(_tlsBootError))
            AppLog.Warn("TLS bootstrap: " + _tlsBootError);

        base.OnStartup(e);
        AppLog.Info("WPF startup completed (MainWindow creating/created)");
    }

    private static string? _tlsBootError;

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("App exit code=" + e.ApplicationExitCode);
        AppLog.Shutdown();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteFatal("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show(
                "EnglishLearning crashed.\n\nDetails were written to:\n" + AppLog.CurrentLogPath + "\n\n" + e.Exception.Message,
                "Hermes.EnglishLearning",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteFatal("AppDomain.UnhandledException (IsTerminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteFatal("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteFatal(string source, Exception? ex)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine("FATAL [" + source + "]");
            if (ex != null)
            {
                text.AppendLine(ex.ToString());
            }

            AppLog.Error(text.ToString().TrimEnd());

            var dir = AppLog.LogDirectory;
            Directory.CreateDirectory(dir);
            var crashPath = Path.Combine(dir, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            File.WriteAllText(crashPath, text.ToString(), Encoding.UTF8);
            AppLog.Error("Crash dump: " + crashPath);
        }
        catch
        {
            try
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hermes.EnglishLearning",
                    "crash_fallback.log");
                Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
                File.AppendAllText(fallback, DateTime.Now.ToString("O") + " " + source + " " + ex + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
