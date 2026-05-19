using System.Windows;
using Hermes.WpGallery.Tool.Services;
using Hermes.WpGallery.Tool.ViewModels;
using Hermes.WpGallery.Tool.Views;

namespace Hermes.WpGallery.Tool;

public partial class App : Application
{
    private MainViewModel?   _mainVm;
    private SettingsService? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.ActivateExistingInstance();
            MessageBox.Show(
                "Hermes.WpGallery.Tool уже запущен.\n\nОкно выведено на передний план.\nЕсли не видите его — проверьте панель задач или завершите процесс в диспетчере задач.",
                "Hermes.WpGallery.Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = new SettingsService();
        var httpFactory = new SimpleHttpClientFactory();
        var fileLog     = new FileLogService();
        var wpService   = new WordPressService(httpFactory, _settings);
        var wpLogSync   = new WordPressLogSyncService(_settings);
        var capture     = new ScreenCaptureService();

        _mainVm = new MainViewModel(_settings, wpService, capture, fileLog, wpLogSync);

        var win = new MainWindow { DataContext = _mainVm };
        MainWindow = win;
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainVm?.Dispose();
        base.OnExit(e);
    }
}
