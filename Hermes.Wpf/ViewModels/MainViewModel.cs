using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private readonly HermesService _hermesService;
    private readonly ProjectService _projectService;
    private readonly HistoryService _historyService;
    private readonly ConnectionService _connectionService;
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly DispatcherTimer _watchdogTimer;
    private bool _isBusy;
    private string _terminalOutput = "Terminal ready.";
    private ConnectionState _currentConnectionState = ConnectionState.Disconnected;
    private string _connectionStatusMessage = "Not checked yet.";
    private bool _isConnectionBusy;
    /// <summary>Suppresses watchdog duplicate terminal+log spam when nothing changed.</summary>
    private string? _lastLoggedConnectionFingerprint;

    public MainViewModel(
        LogService logService,
        HermesService hermesService,
        ProjectService projectService,
        HistoryService historyService,
        ConnectionService connectionService,
        SettingsService settingsService,
        HermesSettings settings)
    {
        _logService = logService;
        _hermesService = hermesService;
        _projectService = projectService;
        _historyService = historyService;
        _connectionService = connectionService;
        _settingsService = settingsService;
        Settings = settings;
        Chat = new ChatViewModel();
        Projects = new ProjectViewModel();
        Projects.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName != nameof(ProjectViewModel.SelectedProject))
            {
                return;
            }

            SnapshotProjectsIntoSettings();

            try
            {
                await _settingsService.SaveAsync(Settings);
            }
            catch (Exception ex)
            {
                _logService.LogError($"[settings] Failed to save projects state: {ex.Message}");
            }

            if (Projects.SelectedProject is not null)
            {
                await LoadProjectHistoryAsync(Projects.SelectedProject);
            }
        };

        RestoreProjectsFromSettings();

        AddProjectCommand = new RelayCommand(_ => AddProject());
        BrowseProjectFolderCommand = new RelayCommand(_ => BrowseProjectFolder());
        SendMessageCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => CanExecuteProjectCommand() && !string.IsNullOrWhiteSpace(Chat.UserInput));
        GatewayRunCommand = new RelayCommand(async _ => await RunQuickActionAsync("gateway run"), _ => CanExecuteProjectCommand());
        StatusCommand = new RelayCommand(async _ => await RunQuickActionAsync("status"), _ => CanExecuteProjectCommand());
        ResetWebhookCommand = new RelayCommand(async _ => await RunQuickActionAsync("gateway reset-webhook"), _ => CanExecuteProjectCommand());
        AnalyzeCodeCommand = new RelayCommand(async _ => await SendMessageTextAsync("Проанализируй код текущего проекта"), _ => CanExecuteProjectCommand());
        ReconnectCommand = new RelayCommand(async _ => await RefreshConnectionAsync(), _ => !_isConnectionBusy);
        SaveSettingsCommand = new RelayCommand(async _ => await _settingsService.SaveAsync(Settings));

        _hermesService.OutputReceived += line =>
        {
            AppendTerminal(line);
        };

        Chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.UserInput))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        };

        Projects.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _watchdogTimer.Tick += async (_, _) => await WatchdogTickAsync();
        _watchdogTimer.Start();

        _logService.LogInfo($"Session initialized. Active log: {_logService.CurrentLogFilePath}");
    }

    public ChatViewModel Chat { get; }
    public ProjectViewModel Projects { get; }
    public HermesSettings Settings { get; }
    public ObservableCollection<string> SessionHistoryTitles { get; } = [];

    public ICommand AddProjectCommand { get; }
    public ICommand BrowseProjectFolderCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand GatewayRunCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand ResetWebhookCommand { get; }
    public ICommand AnalyzeCodeCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public ConnectionState CurrentConnectionState
    {
        get => _currentConnectionState;
        set => SetProperty(ref _currentConnectionState, value);
    }

    public string ConnectionStatusMessage
    {
        get => _connectionStatusMessage;
        set => SetProperty(ref _connectionStatusMessage, value);
    }

    public string TerminalOutput
    {
        get => _terminalOutput;
        set => SetProperty(ref _terminalOutput, value);
    }

    private void SnapshotProjectsIntoSettings()
    {
        Settings.SavedProjectPaths = [.. Projects.Projects.Select(p => p.WindowsPath)];
        Settings.LastSelectedProjectPath = Projects.SelectedProject?.WindowsPath;
    }

    private void RestoreProjectsFromSettings()
    {
        var paths = Settings.SavedProjectPaths ?? [];
        foreach (var raw in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !Directory.Exists(trimmed))
            {
                continue;
            }

            try
            {
                var project = _projectService.BuildProject(trimmed);
                if (Projects.Projects.Any(p =>
                        string.Equals(p.WindowsPath, project.WindowsPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Projects.Projects.Add(project);
            }
            catch (Exception ex)
            {
                _logService.LogError($"[project] Skip restore '{trimmed}': {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(Settings.LastSelectedProjectPath))
        {
            return;
        }

        var target = Settings.LastSelectedProjectPath.Trim();
        var selected = Projects.Projects.FirstOrDefault(p =>
            string.Equals(p.WindowsPath, target, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            Projects.SelectedProject = selected;
        }
    }

    public async Task LoadProjectHistoryAsync(HermesProject project)
    {
        var history = await _historyService.LoadAsync(project.Name);
        Chat.Messages.Clear();

        foreach (var message in history.Messages)
        {
            Chat.Messages.Add(message);
        }
    }

    private bool CanExecuteProjectCommand() => !_isBusy && Projects.SelectedProject is not null;

    public async Task RefreshConnectionAsync()
    {
        if (_isConnectionBusy)
        {
            return;
        }

        _isConnectionBusy = true;
        CurrentConnectionState = ConnectionState.Checking;
        ConnectionStatusMessage = "Checking connection...";

        try
        {
            var status = await _connectionService.RunPreflightAsync(Settings);
            CurrentConnectionState = status.State;
            ConnectionStatusMessage = status.Message;

            var fingerprint = $"{status.State}|{status.Message}";
            if (status.State == ConnectionState.Error && fingerprint == _lastLoggedConnectionFingerprint)
            {
                return;
            }

            if (status.State != ConnectionState.Error)
            {
                _lastLoggedConnectionFingerprint = null;
            }
            else
            {
                _lastLoggedConnectionFingerprint = fingerprint;
            }

            AppendTerminal($"[connection] {status.Message}", status.State is Models.ConnectionState.Error);
        }
        catch (Exception ex)
        {
            CurrentConnectionState = ConnectionState.Error;
            ConnectionStatusMessage = ex.Message;
            AppendTerminal($"[connection error] {ex.Message}", isError: true);
        }
        finally
        {
            _isConnectionBusy = false;
        }
    }

    private void AddProject()
    {
        var raw = Projects.NewProjectPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            AppendTerminal("[project] Укажите путь к папке или нажмите «Обзор…».", isError: true);
            return;
        }

        if (!Directory.Exists(raw))
        {
            AppendTerminal($"[project] Папка не найдена: {raw}", isError: true);
            return;
        }

        var project = _projectService.BuildProject(raw);
        if (Projects.Projects.Any(p => string.Equals(p.WindowsPath, project.WindowsPath, StringComparison.OrdinalIgnoreCase)))
        {
            AppendTerminal($"Project already added: {project.WindowsPath}");
            return;
        }

        Projects.Projects.Add(project);
        Projects.SelectedProject = project;
        Projects.NewProjectPath = string.Empty;
        AppendTerminal($"Project added: {project.Name}");
        CommandManager.InvalidateRequerySuggested();
    }

    private async void BrowseProjectFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку проекта",
            InitialDirectory = ResolveInitialDirectoryHint()
        };

        if (dlg.ShowDialog(Application.Current.MainWindow) != true)
        {
            return;
        }

        var path = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Settings.LastProjectBrowsePath = path;
        Projects.NewProjectPath = path;
        SnapshotProjectsIntoSettings();

        try
        {
            await _settingsService.SaveAsync(Settings);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[settings] Failed to save browse folder: {ex.Message}");
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private string? ResolveInitialDirectoryHint()
    {
        var browse = Settings.LastProjectBrowsePath?.Trim();
        if (!string.IsNullOrEmpty(browse) && Directory.Exists(browse))
        {
            return browse;
        }

        var p = Projects.NewProjectPath?.Trim();
        if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
        {
            return p;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async Task SendMessageAsync()
    {
        var text = Chat.UserInput.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Chat.UserInput = string.Empty;
        await SendMessageTextAsync(text);
    }

    private async Task SendMessageTextAsync(string text)
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Select a project in the left panel (Add Project) before sending.", isError: true);
            return;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        var project = Projects.SelectedProject;
        var wslPath = _projectService.ConvertToWslPath(project.WindowsPath);
        Chat.Messages.Add(new ChatMessage { Role = "User", Text = text });

        try
        {
            var response = await _hermesService.SendMessageAsync(text, wslPath, Settings, Settings.ChatTimeoutSeconds);
            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = response });
            await SaveHistoryAsync(project.Name);
        }
        catch (Exception ex)
        {
            AppendTerminal($"[error] {ex.Message}", isError: true);
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunQuickActionAsync(string command)
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Select a project before running quick actions.", isError: true);
            return;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        var wslPath = _projectService.ConvertToWslPath(Projects.SelectedProject.WindowsPath);
        AppendTerminal($"> hermes {command}");

        try
        {
            var result = await _hermesService.RunQuickActionAsync(command, wslPath, Settings);
            AppendTerminal(result);
        }
        catch (Exception ex)
        {
            AppendTerminal($"[error] {ex.Message}", isError: true);
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task SaveHistoryAsync(string projectName)
    {
        var session = new SessionHistory
        {
            ProjectName = projectName,
            Messages = Chat.Messages.ToList(),
            UpdatedAt = DateTime.Now
        };

        await _historyService.SaveAsync(session);
        if (!SessionHistoryTitles.Contains(projectName))
        {
            SessionHistoryTitles.Add(projectName);
        }
    }

    private void AppendTerminal(string text, bool isError = false)
    {
        var line = $"{text}{Environment.NewLine}";
        var app = Application.Current;

        if (app?.Dispatcher is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => TerminalOutput += line);
        }
        else
        {
            TerminalOutput += line;
        }

        if (isError)
        {
            _logService.LogError(text);
        }
        else
        {
            _logService.LogTerminal(text);
        }
    }

    private async Task WatchdogTickAsync()
    {
        if (!Settings.AutoReconnect || _isConnectionBusy)
        {
            return;
        }

        if (CurrentConnectionState is ConnectionState.Connected or ConnectionState.Checking)
        {
            return;
        }

        await RefreshConnectionAsync();
    }
}
