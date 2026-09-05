using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class MainWindow : Window
{
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
    private WordPressGalleryWindow? _wordPressGalleryWindow;
    private ExternalBrainService? _externalBrainService;
    private ExternalBrainWindow? _externalBrainWindow;
    private SpreadsheetViewerWindow? _spreadsheetViewerWindow;
    private ProjectManagerWindow? _projectManagerWindow;
    private AgentMiniConsoleWindow? _miniConsoleWindow;
    private MainConsoleWindow? _mainConsoleWindow;
    private ProjectManagerDashboardWindow? _portfolioDashboardWindow;
    private TaskDashboardWindow? _taskDashboardWindow;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppVersion.MainWindowTitle;
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

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();

        _externalBrainService = new ExternalBrainService(
            _logService,
            _settings,
            Application.Current!.Dispatcher);

        var vm = new MainViewModel(
            _logService,
            _chatLogService,
            _hermesService,
            _projectService,
            _historyService,
            _connectionService,
            _settingsService,
            _settings,
            _externalBrainService);

        DataContext = vm;
        vm.AttachSaveExperienceOpener(OpenSaveExperienceUi);
        vm.AttachChatWindowOpener(OpenChatWindow);
        vm.AttachProjectManagerWindowOpener(OpenProjectManagerWindow);
        vm.AttachMiniConsoleOpener(OpenMiniConsoleWindow);
        vm.AttachMainConsoleOpener(OpenMainConsoleWindow);
        vm.AttachProjectManagerDashboardOpener(OpenProjectManagerDashboardWindow);
        vm.AttachProjectRelatedWindowOpener(OpenProjectRelatedWindow);
        vm.AttachWordPressGalleryOpener(() => OpenWordPressGallery_OnClick(this, new RoutedEventArgs()));
        vm.SyncWslAgentMemoryToVault("startup");
        vm.SyncPlatformKnowledgeToVault("startup");
        await vm.EnsureSelectedProjectHistoryLoadedAsync();
        OpenChatWindow();
        await vm.RefreshConnectionAsync();

        // Project history loads asynchronously when the selected project is restored; give it a moment
        // before Supabase snapshot/hydration so LoadProjectHistoryAsync does not clear imported rows.
        await Task.Delay(400);
        await vm.InitializeSupabaseRelayAsync();
        await vm.InitializeWhatsAppWebAsync();

        if (_settings.IsFirstRun)
        {
            OpenSetupWizard();
            _settings.IsFirstRun = false;
            await _settingsService.SaveAsync(_settings);
        }

        await PromptMissedScheduledTasksAsync(vm).ConfigureAwait(true);
    }

    private async Task PromptMissedScheduledTasksAsync(MainViewModel vm)
    {
        IReadOnlyList<MissedScheduledTaskInfo> missed;
        try
        {
            missed = vm.DetectMissedScheduledTasks();
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[missed-tasks] detect failed: {ex.Message}");
            return;
        }

        if (missed.Count == 0)
        {
            return;
        }

        foreach (var task in missed)
        {
            var popup = new MissedTaskPopupWindow(task)
            {
                Owner = this,
            };
            var accepted = popup.ShowDialog() == true && popup.RunNowRequested;
            if (!accepted)
            {
                _logService.LogInfo($"[missed-tasks] dismissed: {task.Id}");
                continue;
            }

            _logService.LogInfo($"[missed-tasks] Run Now requested: {task.Id}");
            try
            {
                var (ok, message) = await vm.RunMissedScheduledTaskAsync(task).ConfigureAwait(true);
                MessageBox.Show(
                    this,
                    message,
                    ok ? "Задача выполнена" : "Ошибка выполнения",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _logService.LogError($"[missed-tasks] Run Now failed: {ex.Message}");
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Ошибка Run Now",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ShutdownFlashcardSkillBeforeRelay();
            await vm.SaveCurrentProjectHistoryAsync();
            await vm.ShutdownSupabaseRelayAsync();
            await vm.ShutdownWhatsAppWebAsync();
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

    public void OpenChatWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.Projects.SelectedProject is null)
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
        if (_chatWindow is ChatWindow chatWindow)
        {
            chatWindow.ScrollChatToEnd();
        }
    }

    public void OpenProjectManagerWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_projectManagerWindow is null || !_projectManagerWindow.IsLoaded)
        {
            _projectManagerWindow = new ProjectManagerWindow(vm)
            {
                Owner = this,
            };
            _projectManagerWindow.Show();
            return;
        }

        if (_projectManagerWindow.WindowState == WindowState.Minimized)
        {
            _projectManagerWindow.WindowState = WindowState.Normal;
        }

        _projectManagerWindow.Activate();
    }

    public void OpenMiniConsoleWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_miniConsoleWindow is null || !_miniConsoleWindow.IsLoaded)
        {
            _miniConsoleWindow = new AgentMiniConsoleWindow(
                vm.ActivityBus,
                () => vm.Projects.SelectedProject?.Name)
            {
                Owner = this,
            };
            _miniConsoleWindow.Closed += (_, _) => _miniConsoleWindow = null;
            _miniConsoleWindow.Show();
            return;
        }

        _miniConsoleWindow.Activate();
    }

    public void OpenMainConsoleWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_mainConsoleWindow is null || !_mainConsoleWindow.IsLoaded)
        {
            _mainConsoleWindow = new MainConsoleWindow(vm.ActivityBus)
            {
                Owner = this,
            };
            _mainConsoleWindow.Closed += (_, _) => _mainConsoleWindow = null;
            _mainConsoleWindow.Show();
            return;
        }

        _mainConsoleWindow.Activate();
    }

    public void OpenProjectManagerDashboardWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_portfolioDashboardWindow is null || !_portfolioDashboardWindow.IsLoaded)
        {
            _portfolioDashboardWindow = new ProjectManagerDashboardWindow(vm.PortfolioStore)
            {
                Owner = this,
            };
            _portfolioDashboardWindow.Closed += (_, _) => _portfolioDashboardWindow = null;
            _portfolioDashboardWindow.Show();
            return;
        }

        _portfolioDashboardWindow.Activate();
    }

    private void OpenProjectRelatedWindow(HermesProject project)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var w = new ProjectRelatedWindow(vm, project)
        {
            Owner = this,
        };
        w.Show();
    }

    private void OpenWordPressGallery_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_wordPressGalleryWindow is null || !_wordPressGalleryWindow.IsLoaded)
        {
            _wordPressGalleryWindow = new WordPressGalleryWindow(
                _settings,
                _logService,
                _settingsService,
                vm.GalleryPublisher);
            _wordPressGalleryWindow.Owner = this;
            _wordPressGalleryWindow.Show();
            return;
        }

        if (_wordPressGalleryWindow.WindowState == WindowState.Minimized)
        {
            _wordPressGalleryWindow.WindowState = WindowState.Normal;
        }

        _wordPressGalleryWindow.Activate();
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

    private void OpenSaveExperience_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSaveExperienceUi();
    }

    private void OpenSaveExperienceUi()
    {
        if (_externalBrainService is null || DataContext is not MainViewModel vm)
        {
            return;
        }

        var vault = (_externalBrainService.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (vault.Length == 0 || !System.IO.Directory.Exists(vault))
        {
            MessageBox.Show(
                this,
                "Настройте путь External Brain / Memory или создайте папку.",
                "Hermes · Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var draft = vm.GetLastExperienceDraft()
            ?? TryDraftFromChatMessages(vm)
            ?? new MemoryDraft();

        var projectName = vm.Projects.SelectedProject?.Name?.Trim() ?? string.Empty;
        var w = new MemoryEditorWindow(_externalBrainService, _logService, draft, projectName);
        w.Owner = this;
        w.ShowDialog();
    }

    private static MemoryDraft? TryDraftFromChatMessages(MainViewModel vm)
    {
        try
        {
            var extractor = new MemoryExtractorService();
            return extractor.TryExtractFromMessages(vm.Chat.Messages.ToList());
        }
        catch
        {
            return null;
        }
    }

    private void OpenExternalBrain_OnClick(object sender, RoutedEventArgs e)
    {
        if (_externalBrainService is null)
        {
            return;
        }

        if (_externalBrainWindow is null || !_externalBrainWindow.IsLoaded)
        {
            var evm = new ExternalBrainViewModel(_externalBrainService, _logService);
            _externalBrainWindow = new ExternalBrainWindow(evm);
            _externalBrainWindow.Owner = this;
            _externalBrainWindow.Show();
            return;
        }

        if (_externalBrainWindow.WindowState == WindowState.Minimized)
        {
            _externalBrainWindow.WindowState = WindowState.Normal;
        }

        _externalBrainWindow.Activate();
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logsWindow is null || !_logsWindow.IsLoaded)
        {
            _logsWindow = new LogsWindow(_logService, _settings!, _settingsService);
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

    private void OpenSpreadsheetViewer_OnClick(object sender, RoutedEventArgs e)
    {
        if (_spreadsheetViewerWindow is null || !_spreadsheetViewerWindow.IsLoaded)
        {
            _spreadsheetViewerWindow = new SpreadsheetViewerWindow();
            _spreadsheetViewerWindow.Owner = this;
            _spreadsheetViewerWindow.Show();
            return;
        }

        if (_spreadsheetViewerWindow.WindowState == WindowState.Minimized)
        {
            _spreadsheetViewerWindow.WindowState = WindowState.Normal;
        }

        _spreadsheetViewerWindow.Activate();
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

    private void OpenTaskDashboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_taskDashboardWindow is null || !_taskDashboardWindow.IsLoaded)
        {
            _taskDashboardWindow = new TaskDashboardWindow(
                vm.AgentScheduler,
                t => vm.RunSchedulerTaskNowAsync(t))
            {
                Owner = this,
            };
            _taskDashboardWindow.Closed += (_, _) => _taskDashboardWindow = null;
            _taskDashboardWindow.Show();
            return;
        }

        if (_taskDashboardWindow.WindowState == WindowState.Minimized)
        {
            _taskDashboardWindow.WindowState = WindowState.Normal;
        }

        _taskDashboardWindow.Activate();
    }

    private void OpenSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        var galleryPublisher = DataContext is MainViewModel vmCtx ? vmCtx.GalleryPublisher : null;

        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings, _logService, _settingsService, galleryPublisher);
            _settingsWindow.Owner = this;
            _settingsWindow.Closed += async (_, _) =>
            {
                await _settingsService.SaveAsync(_settings);
                if (_settingsWindow.DataContext is SettingsViewModel settingsVm)
                {
                    settingsVm.SettingsStatusText = SettingsSaveFeedback.FullSettingsSaved(
                        _settingsService.SettingsFilePath,
                        _settings);
                }

                if (DataContext is MainViewModel vm)
                {
                    vm.SyncWslAgentMemoryToVault("settings");
                    vm.SyncPlatformKnowledgeToVault("settings");
                    vm.ReloadAppearanceFromSettings();
                    vm.ReloadGeneratedSkillsCatalog();
                    vm.ApplyWhatsAppMonitoringSettings();
                    await vm.RestartSupabaseRelayAsync();
                    await vm.RestartWhatsAppWebAsync();
                }

                _externalBrainService?.RestartWatcherAndReload("settings");
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
