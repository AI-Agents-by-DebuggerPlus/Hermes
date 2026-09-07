using System.IO;
using System.Windows;
using Hermes.StrategyViewer.Services;
using Hermes.StrategyViewer.ViewModels;

namespace Hermes.StrategyViewer;

public partial class App : Application
{
    private MainViewModel? _vm;
    private LogsWindow? _logsWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                _vm?.Log.Error("DispatcherUnhandledException", args.Exception);
            }
            catch
            {
                // ignore
            }

            MessageBox.Show(args.Exception.ToString(), "StrategyViewer crash");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                _vm?.Log.Error("UnhandledException", args.ExceptionObject as Exception
                    ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));
            }
            catch
            {
                // ignore
            }
        };

        var log = new StrategyLogService();
        _vm = new MainViewModel(log);
        log.Info("Hermes.StrategyViewer starting");

        try
        {
            var path = e.Args.FirstOrDefault(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                       ?? StrategyLoader.ResolveDefaultStrategyPath();
            if (path is null || !File.Exists(path))
            {
                log.Warn("Strategy JSON not found — use File → Open");
            }
            else
            {
                _vm.LoadStrategy(path);
            }

            var main = new MainWindow(_vm, log, OpenLogsWindow);
            MainWindow = main;
            main.Show();
            log.Info($"MainWindow shown: {main.Title}");
        }
        catch (Exception ex)
        {
            log.Error("Startup failed", ex);
            MessageBox.Show(ex.ToString(), "StrategyViewer startup failed");
            Shutdown(1);
        }
    }

    private void OpenLogsWindow()
    {
        if (_logsWindow is { IsLoaded: true })
        {
            _logsWindow.Activate();
            _logsWindow.RefreshLog();
            return;
        }

        _logsWindow = new LogsWindow(_vm!.Log);
        _logsWindow.Owner = Current.MainWindow;
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _vm?.Log.DisposeWriter();
        base.OnExit(e);
    }
}
