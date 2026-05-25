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
        vm.SyncWslAgentMemoryToVault("startup");
        vm.SyncPlatformKnowledgeToVault("startup");
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
            vm.ShutdownFlashcardSkillBeforeRelay();
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
        if (_chatWindow is ChatWindow chatWindow)
        {
            chatWindow.ScrollChatToEnd();
        }
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
                    await vm.RestartSupabaseRelayAsync();
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
