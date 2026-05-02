using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private readonly LogService _logService;
    private readonly HermesService _hermesService;
    private readonly ProjectService _projectService;
    private readonly HistoryService _historyService;
    private readonly ConnectionService _connectionService;
    private readonly SettingsService _settingsService;
    private readonly ChatLogService _chatLogService;
    private HermesSettings? _settings;
    private ChatWindow? _chatWindow;
    private LogsWindow? _logsWindow;
    private HelpWindow? _helpWindow;
    private SetupWizardWindow? _setupWizardWindow;
    private SettingsWindow? _settingsWindow;
    private SupabaseTestChatWindow? _supabaseTestChatWindow;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow()
    {
        InitializeComponent();
        _logService = new LogService();
        _chatLogService = new ChatLogService(_logService);
        _hermesService = new HermesService(_logService);
        _projectService = new ProjectService();
        _historyService = new HistoryService();
        _settingsService = new SettingsService();
        _connectionService = new ConnectionService(_logService, _settingsService);
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }
        catch
        {
            // Non-fatal: window remains usable even if dark title bar fails.
        }
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkTitleBar();
        _settings = await _settingsService.LoadAsync();

        var vm = new MainViewModel(
            _logService,
            _chatLogService,
            _hermesService,
            _projectService,
            _historyService,
            _connectionService,
            _settingsService,
            _settings);

        DataContext = vm;
        OpenChatWindow();
        await vm.RefreshConnectionAsync();

        // Project history loads asynchronously when the selected project is restored; give it a moment
        // before Supabase snapshot/hydration so LoadProjectHistoryAsync does not clear imported rows.
        await Task.Delay(400);
        await vm.InitializeSupabaseRelayAsync();

        if (_settings.IsFirstRun)
        {
            OpenSetupWizard();
            _settings.IsFirstRun = false;
            await _settingsService.SaveAsync(_settings);
        }
    }

    private async void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.ShutdownSupabaseRelayAsync();
        }

        if (_settings is not null)
        {
            await _settingsService.SaveAsync(_settings);
        }
    }

    private void OpenChatButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenChatWindow();
    }

    private void OpenChatWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_chatWindow is null || !_chatWindow.IsLoaded)
        {
            _chatWindow = new ChatWindow(vm);
            _chatWindow.Owner = this;
            _chatWindow.Show();
            return;
        }

        if (_chatWindow.WindowState == WindowState.Minimized)
        {
            _chatWindow.WindowState = WindowState.Normal;
        }

        _chatWindow.Activate();
    }

    private void OpenSupabaseTestChat_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        if (_supabaseTestChatWindow is null || !_supabaseTestChatWindow.IsLoaded)
        {
            _supabaseTestChatWindow = new SupabaseTestChatWindow(_settings, _logService, _settingsService);
            _supabaseTestChatWindow.Owner = this;
            _supabaseTestChatWindow.Show();
            return;
        }

        if (_supabaseTestChatWindow.WindowState == WindowState.Minimized)
        {
            _supabaseTestChatWindow.WindowState = WindowState.Normal;
        }

        _supabaseTestChatWindow.Activate();
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logsWindow is null || !_logsWindow.IsLoaded)
        {
            _logsWindow = new LogsWindow(_logService);
            _logsWindow.Owner = this;
            _logsWindow.Show();
            return;
        }

        if (_logsWindow.WindowState == WindowState.Minimized)
        {
            _logsWindow.WindowState = WindowState.Normal;
        }

        _logsWindow.Activate();
    }

    private void OpenHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsLoaded)
        {
            _helpWindow = new HelpWindow();
            _helpWindow.Owner = this;
            _helpWindow.Show();
            return;
        }

        if (_helpWindow.WindowState == WindowState.Minimized)
        {
            _helpWindow.WindowState = WindowState.Normal;
        }

        _helpWindow.Activate();
    }

    private void OpenSetupButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSetupWizard();
    }

    private void OpenSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.Owner = this;
            _settingsWindow.Closed += async (_, _) =>
            {
                await _settingsService.SaveAsync(_settings);
                if (DataContext is MainViewModel vm)
                {
                    vm.ReloadAppearanceFromSettings();
                    await vm.RestartSupabaseRelayAsync();
                }
            };
            _settingsWindow.Show();
            return;
        }

        _settingsWindow.Activate();
    }

    private void OpenSetupWizard()
    {
        if (_settings is null)
        {
            return;
        }

        if (_setupWizardWindow is null || !_setupWizardWindow.IsLoaded)
        {
            _setupWizardWindow = new SetupWizardWindow(_connectionService, _settingsService, _settings);
            _setupWizardWindow.Owner = this;
            _setupWizardWindow.Show();
            return;
        }

        _setupWizardWindow.Activate();
    }
}
