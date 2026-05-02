using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    private readonly ChatLogService _chatLogService;
    private readonly DispatcherTimer _watchdogTimer;
    private bool _isBusy;
    private string _terminalOutput = "Terminal ready.";
    private ConnectionState _currentConnectionState = ConnectionState.Disconnected;
    private string _connectionStatusMessage = "Not checked yet.";
    private bool _isConnectionBusy;
    /// <summary>Suppresses watchdog duplicate terminal+log spam when nothing changed.</summary>
    private string? _lastLoggedConnectionFingerprint;

    private CancellationTokenSource? _historyLoadCts;

    private double _chatFontSize;

    /// <summary>Set when first Hermes subprocess uses <see cref="HermesSettings.WorkspaceRootWindowsPath"/>.</summary>
    private bool _hermesWorkspaceLogged;

    private readonly SemaphoreSlim _supabasePollGate = new(1, 1);
    private SupabaseChatRelayService? _supabaseRelay;
    private readonly SupabaseHermesEchoTracker _supabaseEchoTracker = new();
    private readonly HashSet<Guid> _supabaseSeenMessageIds = [];
    private DispatcherTimer? _supabaseRelayTimer;
    /// <summary>False until first successful relay fetch completes (polling stays idle before that).</summary>
    private bool _supabasePollingEnabled;

    public MainViewModel(
        LogService logService,
        ChatLogService chatLogService,
        HermesService hermesService,
        ProjectService projectService,
        HistoryService historyService,
        ConnectionService connectionService,
        SettingsService settingsService,
        HermesSettings settings)
    {
        _logService = logService;
        _chatLogService = chatLogService;
        _hermesService = hermesService;
        _projectService = projectService;
        _historyService = historyService;
        _connectionService = connectionService;
        _settingsService = settingsService;
        Settings = settings;
        _chatFontSize = ClampChatFontForUi(Settings.ChatFontSize);
        Settings.ChatFontSize = _chatFontSize;
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

            _historyLoadCts?.Cancel();
            _historyLoadCts?.Dispose();
            _historyLoadCts = new CancellationTokenSource();
            var historyToken = _historyLoadCts.Token;

            if (Projects.SelectedProject is null)
            {
                Chat.Messages.Clear();
                return;
            }

            try
            {
                await LoadProjectHistoryAsync(Projects.SelectedProject, historyToken);
            }
            catch (OperationCanceledException)
            {
                // Switched project before load finished.
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

        LogStartupBanner();
        _logService.LogInfo($"Session initialized. Active log: {_logService.CurrentLogFilePath}");
    }

    private void LogStartupBanner()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "?";
        _logService.LogInfo($"[startup] Hermes.Wpf version={ver}, WSL distro (settings)={Settings.WslDistro}");
        _logService.LogInfo($"[startup] Chat transcript file: {_chatLogService.CurrentChatLogPath}");

        var ws = Settings.WorkspaceRootWindowsPath?.Trim();
        if (!string.IsNullOrEmpty(ws))
        {
            _logService.LogInfo(Directory.Exists(ws)
                ? $"[startup] Hermes workspace root (when set): {Path.GetFullPath(ws)}"
                : $"[startup] Hermes workspace root is set but folder not found: {ws}");
        }

        if (!string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
            && !string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey)
            && !Settings.SupabaseRelayEnabled)
        {
            _logService.LogWarn(
                "[supabase] Заданы URL и anon key, но «Relay enabled» выключен — сообщения из Supabase не попадут в основной чат и ответы не публикуются.");
        }
    }

    public Task InitializeSupabaseRelayAsync() => StartSupabaseRelayCoreAsync();

    /// <summary>When Settings closes or relay options change.</summary>
    public async Task RestartSupabaseRelayAsync()
    {
        await StopSupabaseRelayCoreAsync();
        await StartSupabaseRelayCoreAsync();
    }

    public Task ShutdownSupabaseRelayAsync() => StopSupabaseRelayCoreAsync();

    /// <summary>WPF chat binds here; sync from <see cref="Settings"/> after Settings window closes.</summary>
    public double ChatFontSize
    {
        get => _chatFontSize;
        private set
        {
            var c = ClampChatFontForUi(value);
            Settings.ChatFontSize = c;
            SetProperty(ref _chatFontSize, c);
        }
    }

    public void ReloadAppearanceFromSettings()
    {
        ChatFontSize = ClampChatFontForUi(Settings.ChatFontSize);
    }

    private static double ClampChatFontForUi(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 14;
        }

        const double min = 8;
        const double max = 36;
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    /// <summary>
    /// Hermes <c>bash -lc</c> working directory in WSL form. When <see cref="HermesSettings.WorkspaceRootWindowsPath"/>
    /// exists, all tool access is rooted there; otherwise the selected project folder is used.
    /// </summary>
    private string ResolveHermesWslWorkingDirectory(string projectWindowsPath)
    {
        var raw = Settings.WorkspaceRootWindowsPath?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return _projectService.ConvertToWslPath(projectWindowsPath);
        }

        if (!Directory.Exists(raw))
        {
            return _projectService.ConvertToWslPath(projectWindowsPath);
        }

        try
        {
            var full = Path.GetFullPath(raw);
            if (!_hermesWorkspaceLogged)
            {
                _hermesWorkspaceLogged = true;
                _logService.LogInfo($"[workspace] Hermes процессы используют общий корень: {full}");
            }

            return _projectService.ConvertToWslPath(full);
        }
        catch
        {
            return _projectService.ConvertToWslPath(projectWindowsPath);
        }
    }

    /// <summary>Visible user text stays in UI; optional reminder appended only for <c>hermes chat</c>.</summary>
    private string BuildOutboundHermesPrompt(string userVisibleMessage)
    {
        var blocks = new List<string>();
        blocks.Add(ChatBehaviorDefaults.InstructionPriorityRu);
        blocks.Add(ChatBehaviorDefaults.TaskPrecisionRu);

        if (Settings.AppendVisionScopeReminder && IsVisionRelatedUserMessage(userVisibleMessage))
        {
            blocks.Add(string.IsNullOrWhiteSpace(Settings.VisionScopeReminderNote)
                ? ChatBehaviorDefaults.VisionScopeReminderRu
                : Settings.VisionScopeReminderNote.Trim());
        }

        if (blocks.Count == 0)
        {
            return userVisibleMessage;
        }

        return $"{userVisibleMessage}\n\n---\n[System / Hermes WPF]\n{string.Join("\n\n", blocks)}";
    }

    private static bool IsVisionRelatedUserMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.ToLowerInvariant();
        return t.Contains("экран", StringComparison.Ordinal)
               || t.Contains("интерфейс", StringComparison.Ordinal)
               || t.Contains("скрин", StringComparison.Ordinal)
               || t.Contains("screenshot", StringComparison.Ordinal)
               || t.Contains("screen", StringComparison.Ordinal)
               || t.Contains("vision", StringComparison.Ordinal)
               || t.Contains("картинк", StringComparison.Ordinal)
               || t.Contains("изображени", StringComparison.Ordinal)
               || t.Contains("image", StringComparison.Ordinal);
    }

    private static int ClampChatTimeout(int seconds)
    {
        const int minS = 30;
        const int maxS = 7200;
        if (seconds < minS)
        {
            return minS;
        }

        return seconds > maxS ? maxS : seconds;
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

    public async Task LoadProjectHistoryAsync(HermesProject project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = await _historyService.LoadAsync(project.Name);
        cancellationToken.ThrowIfCancellationRequested();

        Chat.Messages.Clear();

        foreach (var message in history.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private Task SendMessageTextAsync(string text) =>
        ExecuteHermesUserTurnAsync(prependUserBubble: true, agentUserPayload: text, uiUserBubbleLine: null);

    /// <param name="prependUserBubble">Local chat sends <c>true</c>; inbound Supabase already pushed the bubble → <c>false</c>.</param>
    /// <param name="agentUserPayload">Plain user text forwarded to Hermes outbound prompt builder.</param>
    /// <param name="uiUserBubbleLine">When prepending user bubble and null, defaults to payload.</param>
    private async Task ExecuteHermesUserTurnAsync(bool prependUserBubble, string agentUserPayload, string? uiUserBubbleLine = null)
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Select a project in the left panel (Add Project) before sending.", isError: true);
            return;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        var project = Projects.SelectedProject;
        var wslPath = ResolveHermesWslWorkingDirectory(project.WindowsPath);

        if (prependUserBubble)
        {
            var bubbleText = uiUserBubbleLine ?? agentUserPayload;
            Chat.Messages.Add(new ChatMessage { Role = "User", Text = bubbleText });
            _chatLogService.AppendMessage(project.Name, "User", bubbleText);
            await PublishUserTurnToSupabaseIfPossibleAsync(bubbleText);
        }

        try
        {
            var outbound = BuildOutboundHermesPrompt(agentUserPayload);
            var timeout = ClampChatTimeout(Settings.ChatTimeoutSeconds);
            // HermesService uses ConfigureAwait(false) internally — force UI context before touching chat / Supabase.
            var result = await _hermesService.SendMessageAsync(outbound, wslPath, Settings, timeout).ConfigureAwait(true);
            if (!result.Success)
            {
                var hint = PickUserFacingHermesSummary(result);
                AppendTerminal($"[hermes] exit {result.ExitCode}: {hint}", isError: true);
                var errBubble = $"Ошибка CLI (exit {result.ExitCode}): {hint}";
                Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = errBubble });
                _chatLogService.AppendMessage(project.Name, "Hermes", errBubble);
                await PublishAssistantTurnToSupabaseIfPossibleAsync(errBubble);
                await TrySaveHistoryAfterTurnAsync(project.Name);
                return;
            }

            var response = string.IsNullOrWhiteSpace(result.CombinedText) ? "(пустой ответ)" : result.CombinedText;
            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = response });
            _chatLogService.AppendMessage(project.Name, "Hermes", response);
            await PublishAssistantTurnToSupabaseIfPossibleAsync(response);
            await TrySaveHistoryAfterTurnAsync(project.Name);
        }
        catch (Exception ex)
        {
            AppendTerminal($"[error] {ex.Message}", isError: true);
            _chatLogService.AppendMessage(project.Name, "System", $"Exception: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task TrySaveHistoryAfterTurnAsync(string projectName)
    {
        try
        {
            await SaveHistoryAsync(projectName);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[history] Не удалось сохранить историю чата ({projectName}): {ex.Message}");
        }
    }

    private async Task PublishAssistantTurnToSupabaseIfPossibleAsync(string assistantPlainText)
    {
        if (!Settings.SupabaseRelayEnabled)
        {
            if (!string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
                && !string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey))
            {
                _logService.LogWarn(
                    "[supabase] Ответ агента не отправлен в Supabase: включите «Relay enabled» в настройках (заданы URL и anon key).");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(assistantPlainText))
        {
            return;
        }

        await EnsureSupabaseRelayReadyForPublishAsync();

        if (_supabaseRelay is not { IsConnected: true })
        {
            _logService.LogError(
                "[supabase] Ответ агента не отправлен в Supabase: relay не подключён (включите «Relay enabled», проверьте URL/key и лог при старте/в Settings).");
            return;
        }

        var label = CanonicalHermesSenderName();

        try
        {
            await _supabaseRelay.InsertAssistantRowAsync(label, assistantPlainText);
            _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, assistantPlainText);
            _logService.LogInfo(
                $"[supabase] Ответ агента записан в messages (sender_name={label}, chars={assistantPlainText.Length}).");
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Не удалось записать ответ агента в Supabase: {ex.Message}");
        }
    }

    /// <summary>
    /// If relay is enabled but the client is missing (e.g. started with relay off, then enabled without full restart),
    /// reconnect once before INSERT.
    /// </summary>
    private async Task EnsureSupabaseRelayReadyForPublishAsync()
    {
        if (!Settings.SupabaseRelayEnabled)
        {
            return;
        }

        if (_supabaseRelay is { IsConnected: true })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SupabaseUrl) ||
            string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey))
        {
            _logService.LogWarn(
                "[supabase] Не удалось восстановить relay перед публикацией: задайте Supabase URL и anon key в настройках.");
            return;
        }

        _logService.LogWarn("[supabase] Relay: клиент не активен перед публикацией — выполняю StartSupabaseRelayCoreAsync().");
        await StartSupabaseRelayCoreAsync();
    }

    private async Task PublishUserTurnToSupabaseIfPossibleAsync(string userBubbleText)
    {
        if (!Settings.SupabaseRelayEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(userBubbleText))
        {
            return;
        }

        await EnsureSupabaseRelayReadyForPublishAsync();

        if (_supabaseRelay is not { IsConnected: true })
        {
            return;
        }

        var tag = LocalSenderDisplayName();
        if (string.Equals(tag, CanonicalHermesSenderName(), StringComparison.OrdinalIgnoreCase))
        {
            _logService.LogWarn(
                "[supabase] Desktop sender_name совпадает с Assistant sender_name — задайте разные значения в Settings.");
            return;
        }

        try
        {
            await _supabaseRelay.InsertAssistantRowAsync(tag, userBubbleText, CancellationToken.None, logPublish: false);
            _logService.LogInfo($"[supabase] Опубликовано пользовательское сообщение в таблицу messages (sender_name={tag}).");
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Не удалось опубликовать сообщение пользователя: {ex.Message}");
        }
    }

    private string LocalSenderDisplayName()
    {
        var s = Settings.SupabaseLocalSenderName?.Trim();
        return string.IsNullOrEmpty(s) ? "Desktop" : s;
    }

    /// <summary>Our own INSERT echoed by polling — do not append UI or run Hermes again.</summary>
    private bool IsOwnMirroredUserRow(SupabaseMessageRow m)
    {
        if (_supabaseRelay?.CurrentUserId is not { Length: > 0 } uid)
        {
            return false;
        }

        if (!string.Equals((m.SenderId ?? string.Empty).Trim(), uid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            (m.SenderName ?? string.Empty).Trim(),
            LocalSenderDisplayName(),
            StringComparison.OrdinalIgnoreCase);
    }

    private string CanonicalHermesSenderName()
    {
        var s = Settings.SupabaseHermesSenderName?.Trim();
        return string.IsNullOrEmpty(s) ? "Hermes" : s;
    }

    private static bool IsHermesSenderRow(SupabaseMessageRow m, string canonical) =>
        string.Equals((m.SenderName ?? string.Empty).Trim(), canonical.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task StartSupabaseRelayCoreAsync()
    {
        await StopSupabaseRelayCoreAsync();
        _supabaseEchoTracker.Clear();
        _supabasePollingEnabled = false;

        if (!Settings.SupabaseRelayEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SupabaseUrl) ||
            string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey))
        {
            _logService.LogError("[supabase] URL or anon key empty; relay not started.");
            return;
        }

        _supabaseRelay = new SupabaseChatRelayService(_logService);
        try
        {
            await _supabaseRelay.ConnectAsync(Settings.SupabaseUrl.Trim(), Settings.SupabaseAnonKey.Trim());
            if (Settings.SupabaseUseAnonymousAuth)
            {
                await _supabaseRelay.EnsureAnonymousSessionAsync();
            }
            else if (string.IsNullOrWhiteSpace(_supabaseRelay.CurrentUserId))
            {
                _logService.LogWarn(
                    "[supabase] Анонимный вход выключен в Hermes и сессии нет — INSERT в messages часто отклоняется RLS. Включите «Anonymous sign-in» в Hermes или провайдер Anonymous в Supabase.");
            }

            var rows = await _supabaseRelay.FetchAllSortedAsync();
            _supabaseSeenMessageIds.Clear();
            foreach (var m in rows)
            {
                _supabaseSeenMessageIds.Add(m.Id);
            }

            if (Settings.SupabaseImportFullHistoryOnConnect)
            {
                foreach (var m in rows)
                {
                    HydrateSupabaseSnapshotRowIntoChat(m);
                }

                var proj = Projects.SelectedProject;
                if (proj is not null)
                {
                    await SaveHistoryAsync(proj.Name);
                }
            }

            _supabasePollingEnabled = true;
            StartSupabasePollTimer();

            if (string.Equals(
                    LocalSenderDisplayName(),
                    CanonicalHermesSenderName(),
                    StringComparison.OrdinalIgnoreCase))
            {
                _logService.LogWarn(
                    "[supabase] Desktop sender_name и Assistant sender_name совпадают — polling может зациклить агента. Задайте разные имена.");
            }

            var importMode = Settings.SupabaseImportFullHistoryOnConnect ? "full" : "none";
            _logService.LogInfo(
                $"[supabase] Hermes relay: polling on, snapshot rows={rows.Count}, import={importMode}.");
            if (!Settings.SupabaseImportFullHistoryOnConnect && rows.Count > 0)
            {
                _logService.LogInfo(
                    "[supabase] Строки, уже в таблице на момент подключения, помечены как просмотренные и в основной чат не подставляются; " +
                    "появятся только новые сообщения после этого. Чтобы увидеть текущую таблицу в чате, включите «Импорт полной истории при подключении» в настройках Supabase.");
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Connect failed: {ex.Message}");
            _supabaseRelay.Disconnect();
            _supabaseRelay = null;
            _supabasePollingEnabled = false;
        }
    }

    private Task StopSupabaseRelayCoreAsync()
    {
        StopSupabasePollTimerOnly();
        _supabaseRelay?.Disconnect();
        _supabaseRelay = null;
        _supabasePollingEnabled = false;
        _supabaseSeenMessageIds.Clear();
        return Task.CompletedTask;
    }

    private void HydrateSupabaseSnapshotRowIntoChat(SupabaseMessageRow m)
    {
        if (IsOwnMirroredUserRow(m))
        {
            return;
        }

        var canon = CanonicalHermesSenderName();
        if (IsHermesSenderRow(m, canon))
        {
            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = m.Content ?? string.Empty });
            return;
        }

        var name = string.IsNullOrWhiteSpace(m.SenderName) ? "Remote" : m.SenderName.Trim();
        Chat.Messages.Add(new ChatMessage { Role = "User", Text = $"{name}: {m.Content}" });
    }

    private void StartSupabasePollTimer()
    {
        StopSupabasePollTimerOnly();
        var interval = ClampSupabasePollSeconds(Settings.SupabasePollIntervalSeconds);
        _supabaseRelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        _supabaseRelayTimer.Tick += SupabaseRelayTimer_OnTick;
        _supabaseRelayTimer.Start();
    }

    private static int ClampSupabasePollSeconds(int seconds)
    {
        if (seconds < 1)
        {
            return 1;
        }

        return seconds > 120 ? 120 : seconds;
    }

    private void StopSupabasePollTimerOnly()
    {
        if (_supabaseRelayTimer is null)
        {
            return;
        }

        _supabaseRelayTimer.Stop();
        _supabaseRelayTimer.Tick -= SupabaseRelayTimer_OnTick;
        _supabaseRelayTimer = null;
    }

    private async void SupabaseRelayTimer_OnTick(object? sender, EventArgs e) =>
        await PollSupabaseInboxIncrementalOnceAsync();

    private async Task PollSupabaseInboxIncrementalOnceAsync()
    {
        if (!Settings.SupabaseRelayEnabled ||
            !_supabasePollingEnabled ||
            _supabaseRelay is not { IsConnected: true })
        {
            return;
        }

        if (!await _supabasePollGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var rows = await _supabaseRelay.FetchAllSortedAsync();
            foreach (var m in rows.OrderBy(x => x.CreatedAt))
            {
                if (!_supabaseSeenMessageIds.Add(m.Id))
                {
                    continue;
                }

                await HandleInboundSupabaseRowAsync(m);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Poll/incremental fetch failed: {ex.Message}");
        }
        finally
        {
            _supabasePollGate.Release();
        }
    }

    private async Task HandleInboundSupabaseRowAsync(SupabaseMessageRow m)
    {
        var canon = CanonicalHermesSenderName();
        if (IsHermesSenderRow(m, canon))
        {
            if (_supabaseEchoTracker.TryConsumeEcho(m, canon))
            {
                return;
            }

            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = m.Content ?? string.Empty });
            var projHermes = Projects.SelectedProject;
            if (projHermes is not null)
            {
                _chatLogService.AppendMessage(projHermes.Name, "Hermes", m.Content ?? string.Empty);
                await SaveHistoryAsync(projHermes.Name);
            }

            return;
        }

        if (IsOwnMirroredUserRow(m))
        {
            return;
        }

        var remoteName = string.IsNullOrWhiteSpace(m.SenderName) ? "Remote" : m.SenderName.Trim();
        var bubble = $"{remoteName}: {m.Content}";
        var payloadForAgent = string.IsNullOrEmpty((m.Content ?? string.Empty).Trim())
            ? "(пустое сообщение из Supabase)"
            : (m.Content ?? string.Empty).Trim();

        Chat.Messages.Add(new ChatMessage { Role = "User", Text = bubble });

        if (Projects.SelectedProject is null)
        {
            _logService.LogWarn(
                "[supabase] Входящее сообщение из Supabase показано в чате; ответ Hermes не запущен — выберите проект в списке.");
            return;
        }

        _chatLogService.AppendMessage(Projects.SelectedProject.Name, "User", bubble);

        await ExecuteHermesUserTurnAsync(
            prependUserBubble: false,
            agentUserPayload: payloadForAgent);
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
        var wslPath = ResolveHermesWslWorkingDirectory(Projects.SelectedProject.WindowsPath);
        AppendTerminal($"> hermes {command}");

        try
        {
            var quickTimeout = Math.Max(120, ClampChatTimeout(Settings.ChatTimeoutSeconds));
            var result = await _hermesService.RunQuickActionAsync(command, wslPath, Settings, quickTimeout);
            if (!result.Success)
            {
                var hint = PickUserFacingHermesSummary(result);
                AppendTerminal($"[hermes] exit {result.ExitCode}: {hint}", isError: true);
                return;
            }

            var text = string.IsNullOrWhiteSpace(result.CombinedText) ? "(пустой вывод)" : result.CombinedText;
            AppendTerminal(text);
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

    private static string PickUserFacingHermesSummary(HermesExecutionResult result)
    {
        const string timeoutSettingsHint =
            " При необходимости увеличьте «Chat Timeout» в Settings (до 7200 с).";

        string AppendTimeoutHint(string text)
        {
            var timeoutish = result.ExitCode == -1
                             || text.Contains("Timed out", StringComparison.OrdinalIgnoreCase);
            return timeoutish ? text + timeoutSettingsHint : text;
        }

        if (!string.IsNullOrWhiteSpace(result.LastStderrLine))
        {
            var line = result.LastStderrLine.Trim();
            if (line.Length > 400)
            {
                line = line[..400] + "…";
            }

            return AppendTimeoutHint(line);
        }

        var combined = result.CombinedText.Trim();
        if (string.IsNullOrEmpty(combined))
        {
            return AppendTimeoutHint(result.ExitCode == -1 ? "Превышено время ожидания Hermes." : "(нет вывода)");
        }

        var lines = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = lines.Length > 0 ? lines[^1] : combined;
        return AppendTimeoutHint(last);
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
