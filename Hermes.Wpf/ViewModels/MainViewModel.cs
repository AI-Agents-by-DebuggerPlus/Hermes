using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Hermes.DesktopCapture.Models;
using Hermes.InAppAssistant;
using Hermes.InAppAssistant.Wpf;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.Services.WhatsAppWeb;
using Hermes.Wpf.Skills;
using Hermes.Wpf.Views;

namespace Hermes.Wpf.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private static readonly Regex FourPlusDigitRun = new(@"\d{4,}", RegexOptions.Compiled);
    private static readonly Regex FlashcardsViewModePhrase = new(
        @"\bрежим\s+просмотра\s+карточек\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <keywords>Substring match (case-insensitive for message body).</keywords>
    private static readonly string[] VerificationTestKeywords =
    [
        "тест",
        "тестов",
        "верифиц",
        "проверк",
        "провероч",
        "код:",
        " код ",
        "ping",
        "pong",
        "verify",
        "verification",
        "nonce",
        "otp",
    ];

    private readonly HermesService _hermesService;
    private readonly ProjectService _projectService;
    private readonly HistoryService _historyService;
    private readonly ConnectionService _connectionService;
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly ChatLogService _chatLogService;
    private readonly WhatsAppWebLogService _whatsAppLogService;
    private readonly ExternalBrainService _externalBrain;
    private readonly WslAgentMemorySyncService _wslAgentMemorySync;
    private readonly HermesPlatformKnowledgeSyncService _platformKnowledgeSync;
    private readonly WslAgentMemoryService _wslAgentMemoryService;
    private readonly MemoryExtractorService _memoryExtractor = new();
    private MemoryDraft? _lastExperienceDraft;
    private int _lastRoleMemoryCount;
    private IReadOnlyList<string> _lastRoleMemoryTags = [];
    private readonly EnglishTutorVocabularyStore _englishTutorVocabulary = new();
    private readonly EnglishTutorObsidianExporter _englishTutorExporter;
    private Action? _saveExperienceOpener;
    private Action? _openChatWindow;
    private Action? _openProjectManagerWindow;
    private Action? _openMiniConsole;
    private Action? _openMainConsole;
    private Action? _openProjectManagerDashboard;
    private Action<HermesProject>? _openProjectRelatedWindow;
    private Action? _openWordPressGallery;
    private readonly MouseSkillService _mouseSkill;
    private readonly DesktopScreenCaptureService _desktopScreenCapture;
    private readonly DesktopVisionSkill _desktopVisionSkill;
    private readonly DesktopScreenContextStore _desktopScreenContext = new();
    private readonly HermesGalleryPublisher _hermesGalleryPublisher;
    private readonly TradingPlatformBridgeService _tradingBridge;
    private readonly SpotTerminalBridgeService _spotBridge;
    private readonly FuturesTerminalBridgeService _futuresBridge;
    private readonly TradingManualOrderHandler _tradingManualOrder;

    public HermesGalleryPublisher GalleryPublisher => _hermesGalleryPublisher;
    private readonly ReniWaterScriptService _reniWater;
    private readonly ReniWaterSchTasksService _reniSchTasks;
    private readonly ExternalBrainWriteService _externalBrainWriter;
    private readonly LocalExecutionLearningService _localLearning;
    private readonly ReniWaterExecutionCoordinator _reniWaterCoordinator;
    private readonly AgentTaskSchedulerService _agentScheduler;
    private readonly PortfolioStoreService _portfolioStore;
    private readonly AgentActivityBus _activityBus;
    private readonly WpfLocalActionExecutor _wpfLocalExecutor;
    private readonly ReniWaterLocalChatHandler _reniWaterLocalChat;
    private readonly Mt5TerminalIpcClient _mt5TerminalIpc;
    private readonly Mt5TerminalSymbolsService _mt5SymbolsService;
    private readonly SupabaseRemoteLogBridge _remoteLogBridge;
    private readonly HwtStatusSupabasePublisher _hwtStatusPublisher;
    private readonly CliLearningFollowUpService _cliFollowUp;
    private readonly HermesCliSessionStore _cliSessionStore = new();
    private readonly ProjectAgentsBootstrapService _projectAgentsBootstrap;
    private readonly GeneratedSkillCatalogService _generatedSkillCatalog;
    private readonly GeneratedSkillRunner _generatedSkillRunner;
    private readonly SkillGenerationService _skillGeneration;
    private readonly GeneratedSkillVaultSyncService _skillVaultSync;
    private readonly GeneratedSkillIndexService _skillIndex;
    private readonly RoleAwareMemoryRouter _roleMemoryRouter;
    private readonly RoleSkillIndex _roleSkillIndex;
    private readonly RoleManager _roleManager;
    private readonly RoleContextBlockService _roleContextBlockService;
    private readonly RoleExperienceCapture _roleExperienceCapture;
    private readonly TradingExperienceExporter _tradingExperienceExporter;
    private readonly GeneratedSkillTaskMatcher _skillTaskMatcher;
    public GeneratedSkillsViewModel GeneratedSkills { get; }
    private readonly DispatcherTimer _watchdogTimer;
    private readonly DispatcherTimer _reniWaterPollTimer;
    private readonly DispatcherTimer _mt5AgentChatInjectTimer;
    private Mt5TerminalTestCommandsWindow? _mt5TestCommandsWindow;
    private Mt5TerminalTestOrdersWindow? _mt5TestOrdersWindow;
    private bool _mt5AgentChatInjectBusy;
    private bool _reniWaterBusy;
    private bool _isReniWaterStatusBarVisible;
    private string _reniWaterStatusText = string.Empty;
    private string? _reniWaterPendingScreenshotPath;
    private bool _canViewReniWaterScreenshot;
    /// <summary>Last HWT chart PNG successfully published to RemoteTerminal (for local repeat).</summary>
    private string? _lastHwtScreenshotPath;
    private bool _isBusy;
    private string _terminalOutput = "Terminal ready.";
    private ConnectionState _currentConnectionState = ConnectionState.Disconnected;
    private string _connectionStatusMessage = "Not checked yet.";
    private bool _isConnectionBusy;
    /// <summary>Suppresses watchdog duplicate terminal+log spam when nothing changed.</summary>
    private string? _lastLoggedConnectionFingerprint;

    private CancellationTokenSource? _historyLoadCts;
    private Task? _pendingHistoryLoad;
    /// <summary>Project whose messages are currently in Chat.Messages (history bind).</summary>
    private string? _chatHistoryBoundProject;
    /// <summary>Skip SelectedProject side-effects (save/load) while renaming to avoid re-entrancy crash.</summary>
    private bool _suppressProjectSelectionHandler;

    private double _chatFontSize;

    /// <summary>Set when first Hermes subprocess uses <see cref="HermesSettings.WorkspaceRootWindowsPath"/>.</summary>
    private bool _hermesWorkspaceLogged;

    private readonly SemaphoreSlim _supabasePollGate = new(1, 1);
    private SupabaseChatRelayService? _supabaseRelay;
    private SupabaseRealtimeWebSocket? _supabaseRealtime;
    private readonly SupabaseHermesEchoTracker _supabaseEchoTracker = new();
    private readonly HashSet<Guid> _supabaseSeenMessageIds = [];
    private readonly HashSet<string> _supabaseSeenPhotoNonces = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _supabaseRelayTimer;
    /// <summary>False until first successful relay connect; gates optional poll ticks.</summary>
    private bool _supabasePollingEnabled;
    /// <summary>
    /// When set (e.g. inbound from EnglishTutorClient), Hermes replies go to this recipient
    /// instead of the default Android outbound. Not injected into agent prompts.
    /// </summary>
    private string? _supabaseDynamicOutboundRecipient;

    private bool _supabaseRelayToggleBusy;

    private WhatsAppWebWindow? _whatsAppWindow;
    private WhatsAppWebReader? _whatsAppReader;
    private WhatsAppWebMonitorService? _whatsAppMonitor;
    private WhatsAppMonitorReadiness _whatsAppReadiness = WhatsAppMonitorReadiness.Off;
    private string _whatsAppStatusText = string.Empty;

    private bool _agentActivityTracking;
    private bool _agentActivityAssumeExecuting;
    private string _agentChatStatusLine = string.Empty;
    private bool _isAgentChatStatusBarVisible;

    private readonly FlashcardSkill _flashcardSkill;
    private bool _isFlashcardStatusBarVisible;
    private string _flashcardStatusText = string.Empty;
    private bool _flashcardDotPulse;
    private bool _pendingTradingModeSwitch;
    private string? _pendingTradingModeOriginalPayload;
    private bool _skipTradingModeGateOnce;
    private string? _lastPublishedSessionFingerprint;
    private readonly SemaphoreSlim _sessionPublishLock = new(1, 1);
    private string _chatModeStatusText = string.Empty;
    private bool _startupModeNoticePending = true;
    private bool _suppressRoleChangeModeNotice;
    private bool _startupSupabaseNotificationSent;
    private readonly AppAssistantService _appAssistantService;
    private readonly WpfInAppAssistantContextProvider _assistantContextProvider;

    private static readonly Regex HermesStreamLikelyToolActivity = new(
        @"tool_calls?|tool_use|""name""\s*:\s*""|run_terminal|execute_command|function_call|Writing file|Edited file|Applying patch|pwsh|powershell|bash\s+-lc|"
        + @"выполн(яю|ение)\s+команд|запускаю|инструмент|подпроцесс",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public MainViewModel(
        LogService logService,
        ChatLogService chatLogService,
        HermesService hermesService,
        ProjectService projectService,
        HistoryService historyService,
        ConnectionService connectionService,
        SettingsService settingsService,
        HermesSettings settings,
        ExternalBrainService externalBrain)
    {
        _logService = logService;
        _chatLogService = chatLogService;
        _whatsAppLogService = new WhatsAppWebLogService(logService);
        _hermesService = hermesService;
        _projectService = projectService;
        _historyService = historyService;
        _connectionService = connectionService;
        _settingsService = settingsService;
        Settings = settings;
        _externalBrain = externalBrain;
        _roleMemoryRouter = new RoleAwareMemoryRouter();
        _externalBrain.SetRoleRouter(_roleMemoryRouter);
        _roleSkillIndex = new RoleSkillIndex(_logService);
        _roleManager = new RoleManager(_logService, () => Settings, _roleMemoryRouter, _roleSkillIndex);
        _roleContextBlockService = new RoleContextBlockService(() => Settings);
        _roleExperienceCapture = new RoleExperienceCapture(_logService, () => Settings);
        _tradingExperienceExporter = new TradingExperienceExporter(_logService, () => Settings, () => _externalBrain);
        _roleManager.RoleChanged += (_, e) => OnAgentRoleChanged(e);
        _wslAgentMemorySync = new WslAgentMemorySyncService(_logService);
        _platformKnowledgeSync = new HermesPlatformKnowledgeSyncService(_logService);
        _wslAgentMemoryService = new WslAgentMemoryService();
        WslMemory = new WslMemoryViewModel(
            _logService,
            Settings,
            _externalBrain,
            _wslAgentMemoryService,
            _wslAgentMemorySync);
        _englishTutorExporter = new EnglishTutorObsidianExporter(_logService);
        _mouseSkill = new MouseSkillService(Settings, logService);
        _desktopScreenCapture = new DesktopScreenCaptureService(logService, () => Settings);
        _desktopVisionSkill = new DesktopVisionSkill(_hermesService, logService, _projectService, () => Settings);
        _hermesGalleryPublisher = new HermesGalleryPublisher(logService, () => Settings);
        _tradingBridge = new TradingPlatformBridgeService(logService, () => Settings);
        _spotBridge = new SpotTerminalBridgeService(logService, () => Settings);
        _futuresBridge = new FuturesTerminalBridgeService(logService, () => Settings);
        _tradingManualOrder = new TradingManualOrderHandler(_futuresBridge, _spotBridge, logService);
        _tradingExperienceExporter.AttachToBridge(_tradingBridge);
        _reniWater = new ReniWaterScriptService(logService, () => Settings);
        _reniWater.OutputReceived += line => AppendTerminal($"[reni-water] {line}");
        _reniSchTasks = new ReniWaterSchTasksService(logService, () => Settings);
        _generatedSkillRunner = new GeneratedSkillRunner(_logService);
        var skillSandbox = new SkillSandboxService(_logService, _generatedSkillRunner);
        _skillVaultSync = new GeneratedSkillVaultSyncService(_logService);
        _skillIndex = new GeneratedSkillIndexService(_logService);
        _skillTaskMatcher = new GeneratedSkillTaskMatcher(_logService, _roleSkillIndex);
        _generatedSkillCatalog = new GeneratedSkillCatalogService(() => Settings);
        _skillGeneration = new SkillGenerationService(
            _logService,
            () => Settings,
            _generatedSkillRunner,
            skillSandbox,
            _skillVaultSync);
        _externalBrainWriter = new ExternalBrainWriteService(logService);
        _projectAgentsBootstrap = new ProjectAgentsBootstrapService(logService);
        _localLearning = new LocalExecutionLearningService(
            logService,
            () => Settings,
            _memoryExtractor,
            _roleExperienceCapture,
            _externalBrainWriter,
            _externalBrain,
            _wslAgentMemorySync,
            reason => { _ = PersistSettingsQuietAsync(); });
        _reniWaterCoordinator = new ReniWaterExecutionCoordinator(_reniWater, _localLearning, logService);
        _agentScheduler = new AgentTaskSchedulerService(logService);
        _agentScheduler.TaskDue += task => _ = DispatchSchedulerTaskAsync(task);
        _agentScheduler.Start();
        _portfolioStore = new PortfolioStoreService(logService);
        _activityBus = new AgentActivityBus();
        _wpfLocalExecutor = new WpfLocalActionExecutor(
            () => Settings,
            _reniWaterCoordinator,
            _reniWater,
            _reniSchTasks,
            _localLearning,
            _agentScheduler,
            _portfolioStore);
        _reniWaterLocalChat = new ReniWaterLocalChatHandler(_reniWater, _wpfLocalExecutor, logService);
        _mt5TerminalIpc = new Mt5TerminalIpcClient(logService);
        _mt5SymbolsService = new Mt5TerminalSymbolsService(_mt5TerminalIpc, logService);
        _remoteLogBridge = new SupabaseRemoteLogBridge(logService, Settings, () => _supabaseRelay);
        _remoteLogBridge.Start();
        _hwtStatusPublisher = new HwtStatusSupabasePublisher(
            logService,
            Settings,
            () => _supabaseRelay,
            () => Projects.SelectedProject?.WindowsPath);
        _hwtStatusPublisher.Start();
        _cliFollowUp = new CliLearningFollowUpService(_hermesService, logService, () => Settings);
        _chatFontSize = ClampChatFontForUi(Settings.ChatFontSize);
        Settings.ChatFontSize = _chatFontSize;
        Chat = new ChatViewModel();
        var assistantContext = new WpfInAppAssistantContextProvider(BuildInAppAssistantLiveContext);
        _assistantContextProvider = assistantContext;
        _appAssistantService = new AppAssistantService(logger: new WpfAppAssistantLogger(_logService));
        InAppAssistant = new MiniAssistantViewModel(
            _appAssistantService,
            () => new AppAssistantOptions
            {
                ApplicationId = AppAssistantKnowledge.HermesWpfId,
                OpenRouterApiKey = Settings.InAppAssistantOpenRouterApiKey,
                Model = Settings.InAppAssistantOpenRouterModel,
            },
            assistantContext);
        Projects = new ProjectViewModel();
        GeneratedSkills = new GeneratedSkillsViewModel(
            _generatedSkillCatalog,
            _generatedSkillRunner,
            () => Settings,
            _logService,
            line =>
            {
                if (Projects.SelectedProject is null)
                {
                    return;
                }

                Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = line });
                _chatLogService.AppendMessage(Projects.SelectedProject.Name, "Hermes", line);
                _ = PublishAssistantTurnToSupabaseIfPossibleAsync(line);
            });
        _generatedSkillCatalog.CatalogChanged += OnGeneratedSkillsCatalogChanged;
        _generatedSkillCatalog.Reload();
        VaultInitializer.EnsureLayout(_externalBrain.ResolveEffectiveMemoryPath());
        _ = _roleSkillIndex.LoadAsync();
        _roleSkillIndex.Rebuild(_generatedSkillCatalog.Skills);
        Projects.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName != nameof(ProjectViewModel.SelectedProject))
            {
                return;
            }

            if (_suppressProjectSelectionHandler)
            {
                return;
            }

            // Persist the project that currently owns Chat.Messages before switching away.
            await PersistBoundChatHistoryBeforeSwitchAsync().ConfigureAwait(true);

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
                _logService.SetActiveProject(null);
                RaisePropertyChanged(nameof(IsProjectManagerWorkspace));
                CommandManager.InvalidateRequerySuggested();
                RefreshChatModeStatusUi();
                Chat.Messages.Clear();
                _chatHistoryBoundProject = null;
                return;
            }

            _logService.SetActiveProject(Projects.SelectedProject.Name);
            RaisePropertyChanged(nameof(IsProjectManagerWorkspace));
            CommandManager.InvalidateRequerySuggested();
            _projectAgentsBootstrap.EnsureProjectHermesArtifacts(Projects.SelectedProject.WindowsPath);
            if (TradingAnalyticsMt5SymbolsParser.IsTradingAnalyticsProject(Projects.SelectedProject.Name))
            {
                _ = TryEnsureTradingAnalyticsMt5SymbolsAsync(
                    Projects.SelectedProject.WindowsPath,
                    forceRefresh: false);
            }
            RefreshChatModeStatusUi();
            _ = PublishSessionContextToSupabaseIfChangedAsync("project-selected");

            Task? loadTask = null;
            try
            {
                loadTask = LoadProjectHistoryAsync(Projects.SelectedProject, historyToken);
                _pendingHistoryLoad = loadTask;
                await loadTask;
            }
            catch (OperationCanceledException)
            {
                // Switched project before load finished.
            }
            finally
            {
                if (loadTask is not null && ReferenceEquals(_pendingHistoryLoad, loadTask))
                {
                    _pendingHistoryLoad = null;
                }
            }
        };

        RestoreProjectsFromSettings();

        AddProjectCommand = new RelayCommand(_ => AddProject());
        BrowseProjectFolderCommand = new RelayCommand(_ => BrowseProjectFolder());
        RenameProjectCommand = new RelayCommand(async _ => await RenameSelectedProjectAsync(), _ => CanExecuteProjectCommand());
        MoveProjectUpCommand = new RelayCommand(_ => MoveSelectedProject(-1), _ => CanMoveSelectedProject(-1));
        MoveProjectDownCommand = new RelayCommand(_ => MoveSelectedProject(1), _ => CanMoveSelectedProject(1));
        OpenProjectManagerWindowCommand = new RelayCommand(_ => RequestOpenProjectManagerWindow());
        OpenMiniConsoleCommand = new RelayCommand(_ => RequestOpenMiniConsole());
        OpenMainConsoleCommand = new RelayCommand(_ => RequestOpenMainConsole());
        OpenProjectManagerDashboardCommand = new RelayCommand(
            _ => RequestOpenProjectManagerDashboard(),
            _ => IsProjectManagerWorkspace);
        SendMessageCommand = new RelayCommand(
            async _ => await SendMessageAsync(),
            _ => CanExecuteProjectCommand() && CanSendChatMessage());
        AttachChatFileCommand = new RelayCommand(
            _ => AttachChatFilesFromDialog(),
            _ => CanExecuteProjectCommand() && !_isBusy);
        AttachChatScreenshotCommand = new RelayCommand(
            _ => AttachChatScreenshot(),
            _ => CanExecuteProjectCommand() && !_isBusy);
        ClearChatAttachmentsCommand = new RelayCommand(
            _ => ClearPendingChatAttachments(),
            _ => Chat.HasPendingAttachments);
        RemoveChatAttachmentCommand = new RelayCommand(
            p => RemovePendingChatAttachment(p as ChatAttachment),
            p => p is ChatAttachment);
        RetryLastMessageCommand = new RelayCommand(
            async _ => await RetryLastUserMessageAsync(),
            _ => CanExecuteProjectCommand() && FindLastUserMessageText() is not null);
        GatewayRunCommand = new RelayCommand(async _ => await RunQuickActionAsync("gateway run"), _ => CanExecuteProjectCommand());
        StatusCommand = new RelayCommand(async _ => await RunQuickActionAsync("status"), _ => CanExecuteProjectCommand());
        ResetWebhookCommand = new RelayCommand(async _ => await RunQuickActionAsync("gateway reset-webhook"), _ => CanExecuteProjectCommand());
        AnalyzeCodeCommand = new RelayCommand(async _ => await SendMessageTextAsync("Проанализируй код текущего проекта"), _ => CanExecuteProjectCommand());
        OpenMt5TerminalTestCommandsCommand = new RelayCommand(
            _ => OpenMt5TerminalTestCommands(),
            _ => CanOpenMt5TerminalTestCommands());
        OpenMt5TerminalTestOrdersCommand = new RelayCommand(
            _ => OpenMt5TerminalTestOrders(),
            _ => CanOpenMt5TerminalTestOrders());
        ReconnectCommand = new RelayCommand(async _ => await RefreshConnectionAsync(), _ => !_isConnectionBusy);
        SaveSettingsCommand = new RelayCommand(async _ => await _settingsService.SaveAsync(Settings));
        ToggleSupabaseRelayCommand = new RelayCommand(
            async _ => await ToggleSupabaseRelayConnectionAsync(),
            _ => !_supabaseRelayToggleBusy);
        SmokeTestMouseSkillCommand = new RelayCommand(_ => _mouseSkill.RunSmokeShift());
        CaptureDesktopScreenshotCommand = new RelayCommand(
            async _ => await RunDesktopScreenCaptureWithBusyAsync().ConfigureAwait(true),
            _ => !_isBusy);
        SaveExperienceCommand = new RelayCommand(_ => _saveExperienceOpener?.Invoke());
        ResetCliSessionCommand = new RelayCommand(
            _ => ResetCliSessionForCurrentProject(),
            _ => CanExecuteProjectCommand());
        ShowWhatsAppWebWindowCommand = new RelayCommand(_ => ShowWhatsAppWebWindow(), _ => Settings.WhatsAppWebEnabled);
        RestartWhatsAppWebCommand = new RelayCommand(
            async _ => await RestartWhatsAppWebAsync(),
            _ => Settings.WhatsAppWebEnabled && !_isBusy);
        RunWhatsAppParseProbeCommand = new RelayCommand(
            async _ => await RunWhatsAppParseProbeManualAsync(),
            _ => Settings.WhatsAppWebEnabled && _whatsAppMonitor is not null && !_isBusy);
        ExportEnglishTutorProgressCommand = new RelayCommand(_ => ExportEnglishTutorProgress(), _ => true);
        LaunchBinanceDemoSpotCommand = new RelayCommand(_ => LaunchBinanceDemoSpotManual());
        LaunchBinanceDemoFuturesCommand = new RelayCommand(_ => LaunchBinanceDemoFuturesManual());

        _flashcardSkill = new FlashcardSkill(_logService, GenerateFlashcardJsonViaHermesAsync, PublishFlashcardJsonToSupabaseAsync);
        _flashcardSkill.StatusChanged += FlashcardSkill_OnStatusChanged;
        _flashcardSkill.DelayTick += FlashcardSkill_OnDelayTick;
        StopFlashcardsCommand = new RelayCommand(_ => StopFlashcardsInternal(), _ => _flashcardSkill.Status != FlashcardStatus.Idle);

        _roleManager.LoadCurrentRoleFromSettings();
        ApplyAgentRoleToLegacySettings(_roleManager.CurrentRole);

        SubmitReniWaterCommand = new RelayCommand(
            async _ => await SendMessageTextAsync("Передай показания водоканала").ConfigureAwait(true),
            _ => !_isBusy && !_reniWaterBusy);
        AckReniWaterCommand = new RelayCommand(
            async _ => await SendMessageTextAsync("Принял показания Reni Water").ConfigureAwait(true),
            _ => !_isBusy && !_reniWaterBusy);
        LoginReniWaterCommand = new RelayCommand(
            async _ => await SendMessageTextAsync("Открой вход Reni Water").ConfigureAwait(true),
            _ => !_isBusy && !_reniWaterBusy);
        ViewReniWaterScreenshotCommand = new RelayCommand(
            _ => ShowReniWaterScreenshotInChat(),
            _ => _canViewReniWaterScreenshot);
        CheckReniWaterSessionCommand = new RelayCommand(
            async _ => await SendMessageTextAsync("Проверь сессию Reni Water").ConfigureAwait(true),
            _ => !_isBusy && !_reniWaterBusy);

        _hermesService.OutputReceived += OnHermesProcessOutputLine;

        Chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.UserInput)
                || e.PropertyName == nameof(ChatViewModel.HasPendingAttachments))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        };

        Chat.PendingAttachments.CollectionChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
        Projects.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _watchdogTimer.Tick += async (_, _) => await WatchdogTickAsync();
        _watchdogTimer.Start();

        var pollMin = Settings.ReniWaterPendingPollMinutes;
        if (pollMin < 1)
        {
            pollMin = 15;
        }

        _reniWaterPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(pollMin) };
        _reniWaterPollTimer.Tick += (_, _) => RefreshReniWaterPendingUi();
        _reniWaterPollTimer.Start();
        RefreshReniWaterPendingUi();

        // On-demand inject from HWT Cmds… (file drop, not Supabase poll-fallback).
        _mt5AgentChatInjectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mt5AgentChatInjectTimer.Tick += async (_, _) => await TickMt5AgentChatInjectAsync().ConfigureAwait(true);
        _mt5AgentChatInjectTimer.Start();

        _reniSchTasks.ClearDeprecatedInAppScheduleFields();
        _ = PersistSettingsQuietAsync();

        LogStartupBanner();
        _logService.LogInfo($"Session initialized. Active log: {_logService.CurrentLogFilePath}");
        RefreshChatModeStatusUi();
    }

    private void LogStartupBanner()
    {
        _logService.LogInfo($"[startup] Hermes.Wpf {AppVersion.LogStamp}, WSL distro (settings)={Settings.WslDistro}");
        _logService.LogInfo($"[startup] Logs root: {HermesLogPaths.LogsRoot}");
        _logService.LogInfo($"[startup] Session log: {_logService.CurrentLogFilePath}");
        _logService.LogInfo($"[startup] Chat logs: {HermesLogPaths.ChatLogsRoot}");
        _logService.LogInfo($"[startup] WhatsApp logs: {_whatsAppLogService.CurrentLogFilePath}");

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
        else if (Settings.SupabaseRelayEnabled
                 && (string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
                     || string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey)))
        {
            _logService.LogError(
                "[supabase] Relay включён, но URL или anon key пусты — подключение невозможно. "
                + "Settings → Supabase (или перенос из %LocalAppData%\\DesktopVoiceChat\\settings.json при следующем запуске).");
        }
        else if (!string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
                 && !string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey))
        {
            _logService.LogInfo(
                $"[supabase] credentials present (host={LogRedaction.SupabaseHostForLog(Settings.SupabaseUrl)}, relay={(Settings.SupabaseRelayEnabled ? "on" : "off")}).");
        }
    }

    public Task InitializeSupabaseRelayAsync() => StartSupabaseRelayCoreAsync();

    public Task InitializeWhatsAppWebAsync() => StartWhatsAppWebCoreAsync();

    public Task ShutdownWhatsAppWebAsync() => StopWhatsAppWebCoreAsync();

    public async Task RestartWhatsAppWebAsync()
    {
        await StopWhatsAppWebCoreAsync().ConfigureAwait(true);
        await StartWhatsAppWebCoreAsync().ConfigureAwait(true);
    }

    public void ApplyWhatsAppMonitoringSettings()
    {
        if (_whatsAppMonitor is null)
        {
            return;
        }

        _whatsAppMonitor.ApplyForwardingOptions(
            Settings.GetEffectiveWhatsAppMinTextLength(),
            Settings.GetEffectiveWhatsAppTextMarker());
        _whatsAppLogService.LogInfo(
            $"[whatsapp] Settings synced: allow1char={Settings.WhatsAppAllowSingleCharMessages}, minText={Settings.GetEffectiveWhatsAppMinTextLength()}");
    }

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
        RaisePropertyChanged(nameof(HermesAgentPaused));
        RaisePropertyChanged(nameof(EnglishTutorModeEnabled));
        RaisePropertyChanged(nameof(EnglishTutorStatusRibbonText));
        RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
        RaisePropertyChanged(nameof(TradingModeEnabled));
        RaisePropertyChanged(nameof(TradingModeStatusRibbonText));
        RaisePropertyChanged(nameof(IsTradingModeStatusVisible));
        RefreshChatModeStatusUi();
        RaiseSupabaseConnectionUi();
        CommandManager.InvalidateRequerySuggested();
    }

    public void SyncWslAgentMemoryToVault(string reason)
    {
        try
        {
            _wslAgentMemorySync.TrySync(Settings, _externalBrain);
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[wsl-memory-sync] {reason}: {ex.Message}");
        }
    }

    /// <summary>Exports platform memory/skills documentation into External Brain vault.</summary>
    public void SyncPlatformKnowledgeToVault(string reason)
    {
        try
        {
            _platformKnowledgeSync.TrySync(Settings, _externalBrain);
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[platform-knowledge] {reason}: {ex.Message}");
        }
    }

    /// <summary>Persisted режим репетитора EN; переключается ключевыми фразами из чата, сохраняется при смене.</summary>
    public bool EnglishTutorModeEnabled
    {
        get => Settings.EnglishTutorModeEnabled;
        private set
        {
            if (Settings.EnglishTutorModeEnabled == value)
            {
                return;
            }

            Settings.EnglishTutorModeEnabled = value;
            RaisePropertyChanged(nameof(EnglishTutorModeEnabled));
            RaisePropertyChanged(nameof(EnglishTutorStatusRibbonText));
            RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
            RefreshChatModeStatusUi();
            _ = PublishSessionContextToSupabaseIfChangedAsync("english-tutor-mode");
        }
    }

    /// <summary>Second row under connection line in StatusIndicator.</summary>
    public string EnglishTutorStatusRibbonText =>
        EnglishTutorModeEnabled
            ? "Режим репетитора английского — активен (Hermes учит язык)."
            : string.Empty;

    public bool IsEnglishTutorStatusVisible =>
        EnglishTutorModeEnabled && !TradingModeEnabled && _flashcardSkill.Status == FlashcardStatus.Idle;

    public IEnumerable<AgentRole> AvailableAgentRoles => RoleManager.AllRoles;

    public AgentRole CurrentAgentRole
    {
        get => _roleManager.CurrentRole;
        set
        {
            if (_roleManager.CurrentRole == value)
            {
                return;
            }

            _roleManager.SwitchRole(value);
        }
    }

    public string CurrentAgentRoleDisplay => RoleManager.DisplayName(_roleManager.CurrentRole);

    public string CurrentAgentRoleColor => RoleManager.ColorHex(_roleManager.CurrentRole);

    /// <summary>Persisted режим трейдинга; «трейдинг»/«trading» — вход, «режим агента» — выход.</summary>
    public bool TradingModeEnabled
    {
        get => Settings.TradingModeEnabled;
        private set
        {
            if (Settings.TradingModeEnabled == value)
            {
                return;
            }

            Settings.TradingModeEnabled = value;
            if (value)
            {
                Settings.AssistantModeEnabled = false;
            }

            if (value)
            {
                EnsureFuturesTerminalRunning(force: true);
                _logService.LogInfo("[trading-mode] enabled — auto-launch Binance Demo Futures Terminal");
            }

            RaisePropertyChanged(nameof(TradingModeEnabled));
            RaisePropertyChanged(nameof(TradingModeStatusRibbonText));
            RaisePropertyChanged(nameof(IsTradingModeStatusVisible));
            RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
            RefreshChatModeStatusUi();
        }
    }

    public string TradingModeStatusRibbonText =>
        TradingModeEnabled
            ? "📈 Режим трейдинга · трейдер-исполнитель · «режим агента» — выход"
            : string.Empty;

    public bool IsTradingModeStatusVisible =>
        TradingModeEnabled && _flashcardSkill.Status == FlashcardStatus.Idle;

    /// <summary>Main chat routes to OpenRouter assistant instead of WSL hermes.</summary>
    public bool AssistantModeEnabled
    {
        get => Settings.AssistantModeEnabled;
        private set
        {
            if (Settings.AssistantModeEnabled == value)
            {
                return;
            }

            Settings.AssistantModeEnabled = value;
            if (value)
            {
                Settings.TradingModeEnabled = false;
                Settings.EnglishTutorModeEnabled = false;
                _flashcardSkill.Stop();
            }

            RaisePropertyChanged(nameof(AssistantModeEnabled));
            RaisePropertyChanged(nameof(AssistantModeStatusRibbonText));
            RaisePropertyChanged(nameof(IsAssistantModeStatusVisible));
            RefreshChatModeStatusUi();
        }
    }

    public string AssistantModeStatusRibbonText =>
        AssistantModeEnabled
            ? "🤖 Режим ассистента · OpenRouter · «режим агента» — Hermes WSL"
            : string.Empty;

    public bool IsAssistantModeStatusVisible =>
        AssistantModeEnabled && _flashcardSkill.Status == FlashcardStatus.Idle;

    public string ChatModeStatusText
    {
        get => _chatModeStatusText;
        private set => SetProperty(ref _chatModeStatusText, value);
    }

    public bool IsChatModeStatusVisible => Projects.SelectedProject is not null;

    /// <summary>When true, chat + Supabase inbox skip Hermes; UI + outgoing Supabase mirrors still apply.</summary>
    public bool HermesAgentPaused
    {
        get => Settings.HermesAgentPaused;
        set
        {
            if (Settings.HermesAgentPaused == value)
            {
                return;
            }

            Settings.HermesAgentPaused = value;
            RaisePropertyChanged(nameof(HermesAgentPaused));
            _logService.LogInfo(
                value
                    ? "[agent] Пауза включена — Hermes не отвечает на чат и на входящие из Supabase; быстрые действия терминала (Status и т.д.) работают."
                    : "[agent] Пауза снята.");
            _ = PersistHermesAgentPauseSettingAsync();
        }
    }

    private async Task PersistHermesAgentPauseSettingAsync()
    {
        try
        {
            await _settingsService.SaveAsync(Settings);
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[settings] Не удалось сохранить паузу агента: {ex.Message}");
        }
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
    private string BuildOutboundHermesPrompt(
        string userVisibleMessage,
        string? externalBrainContextBlock,
        EnglishTutorTurnHints englishTutor,
        TradingTurnHints trading,
        SkillTurnHints skillTurn)
    {
        var blocks = new List<string>();
        blocks.Add(ChatBehaviorDefaults.InstructionPriorityRu);
        blocks.Add(ChatBehaviorDefaults.TaskPrecisionRu);
        blocks.Add(ChatBehaviorDefaults.ConciseReplyRu);
        blocks.Add(HermesPlatformKnowledgeInstructions.OutboundBlockRu);
        if (!Settings.EnglishTutorModeEnabled)
        {
            blocks.Add(ChatBehaviorDefaults.HermesWpfClientCapabilitiesRu);
            blocks.Add(BuiltInSkillsPromptInstructions.OutboundBlockRu);
        }

        if (!string.IsNullOrWhiteSpace(externalBrainContextBlock))
        {
            blocks.Add(externalBrainContextBlock.Trim());
        }

        if (_roleContextBlockService.IsEnabled && _roleManager.CurrentRole != AgentRole.Universal)
        {
            var roleBlock = _roleContextBlockService.BuildRoleContextBlock(
                _roleManager.CurrentRole,
                _roleManager.CurrentSession,
                _lastRoleMemoryCount,
                _lastRoleMemoryTags);
            if (!string.IsNullOrWhiteSpace(roleBlock))
            {
                blocks.Add(roleBlock);
            }
        }

        if (englishTutor.ExitedThisTurn)
        {
            blocks.Add(EnglishTutorPromptDefaults.OutboundExitNudge);
        }
        else if (Settings.EnglishTutorModeEnabled)
        {
            var recap = $"{_englishTutorVocabulary.CompactSummaryRu()} {_englishTutorVocabulary.ExposureStatsRu()}".Trim();
            blocks.Add(EnglishTutorPromptDefaults.ActivePersonaRu(recap));
            if (englishTutor.EnteredThisTurn)
            {
                blocks.Add(EnglishTutorPromptDefaults.OutboundActivationNudge);
            }
        }

        if (Settings.TradingModeEnabled)
        {
            blocks.Add(TradingModePromptDefaults.ActivePersonaRu);
            var queryIntent = TradingQueryIntentClassifier.Classify(userVisibleMessage);
            var scopeBlock = TradingModePromptDefaults.ScopeInstructionForTurn(queryIntent);
            if (!string.IsNullOrEmpty(scopeBlock))
            {
                blocks.Add(scopeBlock);
            }

            blocks.Add(FuturesTerminalInstructions.OutboundBlockRu);
            AppendFuturesTerminalSnapshotBlocks(blocks);
            var safetyBlock = TradingSafetyRulesInstructions.BuildOutboundBlockRu(Settings.TradingSafetyRulesText);
            if (!string.IsNullOrEmpty(safetyBlock))
            {
                blocks.Add(safetyBlock);
            }
            if (Settings.SpotTerminalIntegrationEnabled)
            {
                blocks.Add(SpotTerminalInstructions.OutboundBlockRu);
                AppendSpotTerminalSnapshotBlocks(blocks);
            }
        }
        else
        {
            blocks.Add(TradingModePromptDefaults.NormalModeGuardRu);
            if (Settings.SpotTerminalIntegrationEnabled)
            {
                AppendSpotTerminalSnapshotBlocks(blocks);
            }

            if (Settings.FuturesTerminalIntegrationEnabled)
            {
                AppendFuturesTerminalSnapshotBlocks(blocks);
            }
        }

        if (Settings.AppendVisionScopeReminder && IsVisionRelatedUserMessage(userVisibleMessage))
        {
            blocks.Add(string.IsNullOrWhiteSpace(Settings.VisionScopeReminderNote)
                ? ChatBehaviorDefaults.VisionScopeReminderRu
                : Settings.VisionScopeReminderNote.Trim());
        }

        if (LooksLikeVerificationTestMessage(userVisibleMessage))
        {
            blocks.Add(ChatBehaviorDefaults.VerificationCodeEchoRu);
        }

        if (Settings.SupabaseRelayEnabled)
        {
            blocks.Add(FlashcardRelayInstructions.OutboundBlockRu);
            if (!Settings.EnglishTutorModeEnabled && !Settings.TradingModeEnabled)
            {
                blocks.Add(AndroidTtsSupabaseInstructions.OutboundBlockRu);
            }
        }

        if (Settings.SkillGenerationEnabled)
        {
            if (skillTurn.TaskMatches is { Count: > 0 })
            {
                blocks.Add(SkillResolverInstructions.TaskMatchBlockRu(skillTurn.TaskMatches));
            }

            blocks.Add(SkillGenerationInstructions.OutboundBlockRu);
            var catalogHint = _generatedSkillCatalog.CompactCatalogForPrompt();
            if (!string.IsNullOrWhiteSpace(catalogHint))
            {
                blocks.Add(catalogHint);
            }

            foreach (var genBlock in _generatedSkillCatalog.OutboundPromptBlocks())
            {
                blocks.Add(genBlock);
            }

            if (skillTurn.CrystallizeRequested)
            {
                var ctx = skillTurn.ReflectionContext ?? SkillReflectionService.BuildFromMessages(Chat.Messages);
                blocks.Add(SkillReflectionService.CrystallizeNowBlockRu(ctx));
                _logService.LogInfo("[skill-gen] reflective crystallization block appended to outbound prompt");
            }
        }

        var desktopBlock = _desktopScreenContext.BuildOutboundInjectionBlock();
        if (!string.IsNullOrWhiteSpace(desktopBlock) && ShouldInjectDesktopContext(userVisibleMessage))
        {
            blocks.Add(ChatBehaviorDefaults.DesktopScreenContextInjectionRu);
            blocks.Add(desktopBlock);
        }

        if (blocks.Count == 0)
        {
            return userVisibleMessage;
        }

        var combined = $"{userVisibleMessage}\n\n---\n[System / Hermes WPF]\n{string.Join("\n\n", blocks)}";
        if (Settings.DiagnosticLogHermesCommands)
        {
            _logService.LogInfo(
                $"[chat-outbound] EnglishTutorMode={Settings.EnglishTutorModeEnabled} payloadChars={combined.Length}");
        }

        return combined;
    }

    private string BuildOutboundDesktopVisionPrompt(string visionUserRequest)
    {
        var blocks = new List<string>
        {
            ChatBehaviorDefaults.InstructionPriorityRu,
            ChatBehaviorDefaults.TaskPrecisionRu,
            ChatBehaviorDefaults.DesktopVisionOutboundRu,
        };

        if (!Settings.EnglishTutorModeEnabled)
        {
            blocks.Add(ChatBehaviorDefaults.HermesWpfClientCapabilitiesRu);
        }

        return $"{visionUserRequest}\n\n---\n[System / Hermes WPF]\n{string.Join("\n\n", blocks)}";
    }

    private void OnGeneratedSkillsCatalogChanged()
    {
        RaisePropertyChanged(nameof(AgentSkills));
        GeneratedSkills.Refresh();
        _skillTaskMatcher.Rebuild(_generatedSkillCatalog.Skills);
        _roleSkillIndex.Rebuild(_generatedSkillCatalog.Skills);
        _skillIndex.WriteIndex(Settings, _generatedSkillCatalog.Skills);
        _skillVaultSync.SyncAll(_externalBrain, _generatedSkillCatalog.Skills);
    }

    private void OnAgentRoleChanged(RoleChangedEventArgs e)
    {
        ApplyAgentRoleToLegacySettings(e.Current);
        if (e.Current == AgentRole.Trader)
        {
            EnsureFuturesTerminalRunning(force: true);
            _logService.LogInfo("[role-manager] Trader — auto-launch Binance Demo Futures Terminal");
        }

        if (!_suppressRoleChangeModeNotice && Projects.SelectedProject is { Name: var projectName })
        {
            PostModeStatusNotice(projectName);
        }

        _suppressRoleChangeModeNotice = false;

        _ = _roleManager.SaveCurrentRoleAsync();
        _ = PublishSessionContextToSupabaseIfChangedAsync("role-changed");
    }

    private void ApplyAgentRoleToLegacySettings(AgentRole role)
    {
        Settings.TradingModeEnabled = role == AgentRole.Trader;
        Settings.EnglishTutorModeEnabled = role == AgentRole.EnglishTutor;
        if (role is AgentRole.Trader or AgentRole.EnglishTutor)
        {
            Settings.AssistantModeEnabled = false;
        }

        _roleMemoryRouter.CurrentRole = role;
        RaisePropertyChanged(nameof(TradingModeEnabled));
        RaisePropertyChanged(nameof(EnglishTutorModeEnabled));
        RaisePropertyChanged(nameof(AssistantModeEnabled));
        RaisePropertyChanged(nameof(TradingModeStatusRibbonText));
        RaisePropertyChanged(nameof(IsTradingModeStatusVisible));
        RaisePropertyChanged(nameof(EnglishTutorStatusRibbonText));
        RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
        RaisePropertyChanged(nameof(CurrentAgentRole));
        RaisePropertyChanged(nameof(CurrentAgentRoleDisplay));
        RaisePropertyChanged(nameof(CurrentAgentRoleColor));
        RefreshChatModeStatusUi();
    }

    public void ReloadGeneratedSkillsCatalog()
    {
        _generatedSkillCatalog.Reload();
    }

    private readonly struct EnglishTutorTurnHints(bool exitedThisTurn, bool enteredThisTurn)
    {
        public bool ExitedThisTurn { get; } = exitedThisTurn;
        public bool EnteredThisTurn { get; } = enteredThisTurn;
    }

    private readonly struct TradingTurnHints(bool exitedThisTurn, bool enteredThisTurn)
    {
        public bool ExitedThisTurn { get; } = exitedThisTurn;
        public bool EnteredThisTurn { get; } = enteredThisTurn;
    }

    private void AppendFuturesTerminalSnapshotBlocks(List<string> blocks)
    {
        if (!_futuresBridge.IsActiveForSession)
        {
            return;
        }

        EnsureFuturesTerminalRunning(force: true);
        var futuresBlock = _futuresBridge.BuildFuturesContextBlockRu();
        if (!string.IsNullOrWhiteSpace(futuresBlock))
        {
            blocks.Add(futuresBlock);
        }
        else if (_futuresBridge.IsTerminalAlive())
        {
            blocks.Add("### Binance Demo Futures Terminal\nТерминал активен, snapshot пуст — дождитесь обновления bridge.");
        }
        else
        {
            blocks.Add(
                "### Binance Demo Futures Terminal\n"
                + "Hermes.BinanceDemoFuturesTerminal не запущен. Нажмите Binance Futures или включите FuturesTerminalAutoLaunch.");
        }
    }

    private void AppendSpotTerminalSnapshotBlocks(List<string> blocks)
    {
        if (!_spotBridge.IsActiveForSession)
        {
            return;
        }

        EnsureSpotTerminalRunning(force: true);
        var spotBlock = _spotBridge.BuildSpotContextBlockRu();
        if (!string.IsNullOrWhiteSpace(spotBlock))
        {
            blocks.Add(spotBlock);
        }
        else if (_spotBridge.IsTerminalAlive())
        {
            blocks.Add("### Spot Terminal\nТерминал активен, snapshot пуст — дождитесь обновления bridge.");
        }
        else
        {
            blocks.Add(
                "### Spot Terminal\n"
                + "Hermes.SpotTerminal не запущен. Запустите Hermes.SpotTerminal.exe или включите SpotTerminalAutoLaunch.");
        }
    }

    private void AppendTradingPlatformSnapshotBlocks(List<string> blocks)
    {
        if (!_tradingBridge.IsIntegrationEnabled)
        {
            return;
        }

        blocks.Add(TradingPlatformInstructions.OutboundBlockRu);
        _tradingBridge.EnsureTerminalRunning(force: true);
        if (_tradingBridge.IsTerminalAlive())
        {
            var snap = _tradingBridge.TryReadSnapshot();
            if (snap is not null)
            {
                blocks.Add(_tradingBridge.BuildSnapshotContextBlockRu(snap));
            }
            else
            {
                blocks.Add("### Trading Platform\nТерминал запущен, но snapshot недоступен. Подождите обновления bridge.");
            }
        }
        else
        {
            blocks.Add(
                "### Trading Platform\n"
                + "Терминал не запущен (нет свежего heartbeat). Запустите Hermes.TradingPlatform.exe "
                + "или включите автозапуск в Settings.");
        }
    }

    private readonly struct SkillTurnHints(
        bool crystallizeRequested,
        string? reflectionContext,
        IReadOnlyList<SkillTaskMatch>? taskMatches)
    {
        public bool CrystallizeRequested { get; } = crystallizeRequested;
        public string? ReflectionContext { get; } = reflectionContext;
        public IReadOnlyList<SkillTaskMatch>? TaskMatches { get; } = taskMatches;
    }

    private async Task PersistSettingsQuietAsync()
    {
        try
        {
            await _settingsService.SaveAsync(Settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[settings] save after English tutor toggle: {ex.Message}");
        }
    }

    /// <summary>
    /// Default agent chat: Hermes CLI only — no WPF prompt injection, chat parsing, or parallel memory/skills.
    /// </summary>
    private bool IsPureHermesAgentChatTurn() =>
        !Settings.AssistantModeEnabled
        && !Settings.TradingModeEnabled
        && !Settings.EnglishTutorModeEnabled
        && _flashcardSkill.Status == FlashcardStatus.Idle;

    private async Task<HermesExecutionResult> SendHermesChatWithSessionAsync(
        string projectName,
        string payload,
        string wslPath,
        int timeout,
        bool continueCliSession = true)
    {
        var resumeId = continueCliSession ? _cliSessionStore.GetSessionId(projectName) : null;
        if (continueCliSession)
        {
            _logService.LogInfo(resumeId is null
                ? "[agent] CLI session new"
                : $"[agent] CLI session resume={resumeId}");
        }

        async Task<HermesExecutionResult> InvokeAsync(string? sessionId) =>
            await _hermesService
                .SendMessageAsync(payload, wslPath, Settings, timeout, sessionId)
                .ConfigureAwait(true);

        var result = await InvokeAsync(resumeId).ConfigureAwait(true);

        if (!result.Success
            && continueCliSession
            && resumeId is not null
            && HermesChatResponseParser.IsSessionNotFound(result.CombinedText))
        {
            _logService.LogWarn($"[agent] CLI session {resumeId} not found — starting new session");
            _cliSessionStore.ClearSessionId(projectName);
            result = await InvokeAsync(null).ConfigureAwait(true);
        }

        if (result.Success && continueCliSession && !string.IsNullOrWhiteSpace(result.SessionId))
        {
            _cliSessionStore.SetSessionId(projectName, result.SessionId);
        }

        return result;
    }

    private void ResetCliSessionForCurrentProject()
    {
        if (Projects.SelectedProject is not { Name: var name })
        {
            return;
        }

        ResetCliSessionForProject(name);
        PostCliSessionResetReply(name);
    }

    private void ResetCliSessionForProject(string projectName)
    {
        _cliSessionStore.ClearSessionId(projectName);
        _logService.LogInfo($"[agent] CLI --resume session cleared for project «{projectName}»");
    }

    private void PostCliSessionResetReply(string projectName)
    {
        PostLocalHermesReply(
            projectName,
            "CLI-сессия Hermes сброшена. Следующее сообщение начнёт новый контекст (без --resume).",
            publishToSupabase: false);
    }

    private async Task ExecutePureCliAgentTurnAsync(string projectName, string userPayload, string wslPath)
    {
        var payload = (userPayload ?? string.Empty).Trim();
        if (payload.Length == 0)
        {
            return;
        }

        _logService.LogInfo("[agent] pure CLI pass-through (no WPF prompt blocks, no post-parse, no WPF memory)");

        _projectAgentsBootstrap.EnsureProjectHermesArtifacts(
            Projects.SelectedProject?.WindowsPath ?? projectName);

        var timeout = ClampChatTimeout(Settings.ChatTimeoutSeconds);
        var result = await SendHermesChatWithSessionAsync(projectName, payload, wslPath, timeout)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            var hint = PickUserFacingHermesSummary(result);
            AppendTerminal($"[hermes] exit {result.ExitCode}: {hint}", isError: true);
            var errBubble = $"Ошибка CLI (exit {result.ExitCode}): {hint}";
            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = errBubble });
            _chatLogService.AppendMessage(projectName, "Hermes", errBubble);
            NotifyHermesCliFailure(projectName, result, hint);
            await PublishAssistantTurnToSupabaseIfPossibleAsync(errBubble);
            await TrySaveHistoryAfterTurnAsync(projectName);
            return;
        }

        var rawResponse = await ResolveMt5TerminalRouterChatAsync(projectName, result, payload).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            rawResponse = string.IsNullOrWhiteSpace(result.EffectiveDisplayText)
                ? "(пустой ответ)"
                : result.EffectiveDisplayText;
        }

        TradingAnalyticsSignalCard? tradeSignal = null;
        var displaySource = rawResponse;
        if (TradingAnalyticsSignalParser.IsTradingAnalyticsProject(projectName))
        {
            if (TradingAnalyticsMt5SymbolsParser.TryParseFetchRequest(rawResponse, out var forceSymbolsRefresh))
            {
                displaySource = TradingAnalyticsMt5SymbolsParser.StripFromDisplay(displaySource);
                _logService.LogInfo("[mt5-symbols] agent requested Mt5Terminal symbol catalog fetch");
                await TryEnsureTradingAnalyticsMt5SymbolsAsync(
                    Projects.SelectedProject?.WindowsPath ?? projectName,
                    forceSymbolsRefresh).ConfigureAwait(true);
            }

            if (TradingAnalyticsSignalParser.TryParseObjectFromJsonOnly(rawResponse, out var jsonSignal))
            {
                tradeSignal = jsonSignal;
                displaySource = TradingAnalyticsSignalParser.StripSignalJsonFromDisplay(rawResponse);
                _logService.LogInfo("[trade-signal] parsed from trade_signal JSON");
            }
            else if (TradingAnalyticsSignalParser.TryParseFromStructuredText(rawResponse, out var textSignal))
            {
                tradeSignal = textSignal;
                _logService.LogInfo("[trade-signal] parsed from markdown (agent omitted trade_signal JSON)");
            }
        }

        var displayResponse = HermesReplySplit.ForChatDisplay(displaySource);

        // Narrow exception: scheduler_* / portfolio_* even in pure CLI mode.
        if (WpfLocalIntentParser.TryConsumeIntent(rawResponse, out var harnessIntent)
            && harnessIntent is not null
            && IsWpfLocalHarnessAction(harnessIntent.Action))
        {
            var (schedDisplay, _, _) = await ProcessWpfLocalTurnAsync(
                rawResponse,
                harnessIntent,
                projectName,
                payload,
                wslPath,
                "cli-harness").ConfigureAwait(true);
            displayResponse = schedDisplay;
        }

        Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = displayResponse });
        _chatLogService.AppendMessage(projectName, "Hermes", displayResponse);
        NotifyHermesReplyArrived(projectName, displayResponse);
        await PublishAssistantTurnToSupabaseIfPossibleAsync(rawResponse);
        await TrySaveHistoryAfterTurnAsync(projectName);
        RequestChatScrollToBottom();

        if (tradeSignal is not null)
        {
            await TryOfferTradingAnalyticsSignalApprovalAsync(projectName, tradeSignal).ConfigureAwait(true);
        }
    }

    private static bool IsWpfLocalHarnessAction(string action) =>
        action.StartsWith("scheduler_", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("portfolio_", StringComparison.OrdinalIgnoreCase);

    private async Task TryOfferTradingAnalyticsSignalApprovalAsync(
        string projectName,
        TradingAnalyticsSignalCard signal)
    {
        var mt5Path = ResolveMt5TerminalProjectPath();
        var chartSymbol = Mt5TerminalIpcClient.TryReadChartSymbol(mt5Path);
        _logService.LogInfo(
            $"[trade-signal] Parsed {signal.Symbol} {signal.PendingOrderTypeLabel} entry={signal.EffectiveEntry} chart={chartSymbol ?? "?"}");

        var approved = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var owner = Application.Current.MainWindow;
            var dlg = new TradeSignalApprovalWindow(signal, chartSymbol)
            {
                Owner = owner,
            };
            return dlg.ShowDialog() == true;
        }).Task.ConfigureAwait(true);

        if (!approved)
        {
            _logService.LogInfo("[trade-signal] User cancelled Apply.");
            AppendTerminal("[trade-signal] Сигнал отменён (Cancel).");
            return;
        }

        var route = TradingAnalyticsSignalExecutor.ToMt5Command(signal);
        try
        {
            var exec = await _mt5TerminalIpc
                .ExecuteAsync(route, mt5Path, TimeSpan.FromSeconds(30))
                .ConfigureAwait(true);
            var msg = Mt5TerminalIpcClient.FormatChatMessage(route, exec);
            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = msg });
            _chatLogService.AppendMessage(projectName, "Hermes", msg);
            await TrySaveHistoryAfterTurnAsync(projectName).ConfigureAwait(true);
            RequestChatScrollToBottom();
            AppendTerminal(
                $"[trade-signal] Apply → Mt5Terminal ok={exec.Ok} {exec.Error ?? exec.Message ?? string.Empty}",
                isError: !exec.Ok);
        }
        catch (Exception ex)
        {
            _logService.LogError("[trade-signal] " + ex.Message);
            AppendTerminal("[trade-signal] Apply failed: " + ex.Message, isError: true);
        }
    }

    private string? ResolveMt5TerminalProjectPath()
    {
        var project = Projects.Projects.FirstOrDefault(p =>
            Mt5TerminalTradeRouter.IsMt5TerminalProject(p.Name));
        if (project is not null)
        {
            return project.WindowsPath;
        }

        return @"D:\Programming\AI_Agents\HermesProjects\Mt5Terminal";
    }

    private async Task TryEnsureTradingAnalyticsMt5SymbolsAsync(string? tradingAnalyticsProjectPath, bool forceRefresh)
    {
        if (string.IsNullOrWhiteSpace(tradingAnalyticsProjectPath))
        {
            return;
        }

        if (!forceRefresh && !_mt5SymbolsService.ShouldRefreshCache(tradingAnalyticsProjectPath))
        {
            var existing = _mt5SymbolsService.TryLoadCache(tradingAnalyticsProjectPath);
            if (existing is not null)
            {
                _logService.LogInfo($"[mt5-symbols] using cache ({existing.Count} symbols, {existing.FetchedAtUtc:u})");
                return;
            }
        }

        var mt5Path = ResolveMt5TerminalProjectPath();
        try
        {
            var catalog = await _mt5SymbolsService
                .EnsureCachedAsync(tradingAnalyticsProjectPath, mt5Path, forceRefresh)
                .ConfigureAwait(true);
            if (catalog is null)
            {
                AppendTerminal(
                    "[mt5-symbols] Не удалось получить список символов из Mt5Terminal (HWT открыт? EA на графике?)",
                    isError: true);
                return;
            }

            AppendTerminal(
                $"[mt5-symbols] Кэш обновлён: {catalog.Count} символов → {Mt5TerminalSymbolsService.ResolveCachePath(tradingAnalyticsProjectPath)}");
        }
        catch (Exception ex)
        {
            _logService.LogError("[mt5-symbols] " + ex.Message);
            AppendTerminal("[mt5-symbols] Ошибка: " + ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Trading Analytics local smoke test: show Apply/Cancel for a fixed XAUUSD Sell Limit card (no Hermes CLI).
    /// Triggers: «тестовая карточка сигнала», «test trade signal».
    /// </summary>
    private async Task<bool> TryHandleTradingAnalyticsTestSignalLocalAsync(string userPayload, string projectName)
    {
        if (!TradingAnalyticsSignalParser.IsTradingAnalyticsProject(projectName))
        {
            return false;
        }

        var t = (userPayload ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return false;
        }

        var hit = t is "тестовая карточка" or "тестовая карточка сигнала" or "test trade signal" or "test signal card"
                  || t.Contains("тестовая карточка сигнала", StringComparison.Ordinal)
                  || t.Contains("test trade signal", StringComparison.Ordinal);
        if (!hit)
        {
            return false;
        }

        var card = new TradingAnalyticsSignalCard
        {
            SignalId = "test-xauusd-sell-limit-" + DateTime.UtcNow.ToString("HHmmss"),
            Symbol = "XAUUSD",
            Side = "sell",
            OrderType = "limit",
            Entry = 4490,
            StopLoss = 4505,
            TakeProfit = 4450,
            Lot = 0.01,
            Timeframe = "H1",
            Summary = "TEST: Sell Limit XAUUSD (local smoke test — Apply → place_pending)",
        };

        var info =
            "Тестовая карточка XAUUSD Sell Limit entry=4490 SL=4505 TP=4450 lot=0.01.\n"
            + "График HWT должен быть XAUUSD. Real trading ON + Algo Trading в MT5.\n"
            + "Сейчас откроется Apply / Cancel.";
        Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = info });
        _chatLogService.AppendMessage(projectName, "Hermes", info);
        RequestChatScrollToBottom();
        _logService.LogInfo("[trade-signal] local test card XAUUSD Sell Limit");
        await TryOfferTradingAnalyticsSignalApprovalAsync(projectName, card).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Mt5Terminal local «повтор» / repeat: re-send last published HWT screenshot to RemoteTerminal.
    /// No Hermes CLI, no new ChartScreenShot IPC.
    /// </summary>
    private async Task<bool> TryHandleMt5TerminalRepeatLocalAsync(string userPayload, string projectName)
    {
        if (!Mt5TerminalTradeRouter.IsMt5TerminalProject(projectName))
        {
            return false;
        }

        if (!Mt5TerminalTradeRouter.LooksLikeRepeatRequest(userPayload))
        {
            return false;
        }

        _logService.LogInfo("[mt5-repeat] local (no Hermes CLI)");

        var path = _lastHwtScreenshotPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var miss =
                """
                [info]
                Нет сохранённого скриншота для повтора. Сначала отправьте Screenshot.

                [Voice]
                {"ru":"Нет сохранённого скриншота."}
                [/Voice]
                """;
            PostMt5TerminalLocalReply(projectName, miss.Trim());
            AppendTerminal("[mt5-repeat] no last screenshot path", isError: true);
            return true;
        }

        try
        {
            if (_supabaseRelay is not { IsConnected: true })
            {
                var offline =
                    """
                    [info]
                    Relay offline — RemoteTerminal не уведомлён.

                    [Voice]
                    {"ru":"Relay offline."}
                    [/Voice]
                    """;
                PostMt5TerminalLocalReply(projectName, offline.Trim());
                AppendTerminal("[mt5-repeat] relay offline", isError: true);
                return true;
            }

            var shot = await _supabaseRelay.PublishHwtScreenshotAsync(path, replay: true).ConfigureAwait(true);
            AppendTerminal($"[mt5-repeat] republish ok={shot.Ok} {shot.Message}", isError: !shot.Ok);

            if (shot.Ok)
            {
                var ok =
                    """
                    [info]
                    Повтор: последний скриншот снова отправлен в RemoteTerminal.

                    [Voice]
                    {"ru":"Повтор скриншота."}
                    [/Voice]
                    """;
                PostMt5TerminalLocalReply(projectName, ok.Trim());
            }
            else
            {
                var fail =
                    "[info]\nПовтор не удался: " + shot.Message
                    + "\n\n[Voice]\n{\"ru\":\"Повтор не удался.\"}\n[/Voice]";
                PostMt5TerminalLocalReply(projectName, fail);
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarn("[mt5-repeat] " + ex.Message);
            PostMt5TerminalLocalReply(
                projectName,
                "[info]\nПовтор: " + ex.Message + "\n\n[Voice]\n{\"ru\":\"Ошибка повтора.\"}\n[/Voice]");
            AppendTerminal("[mt5-repeat] " + ex.Message, isError: true);
        }

        return true;
    }

    /// <summary>
    /// Mt5Terminal local «refresh»: IPC snapshot + publish <c>hwt_status</c> to RemoteTerminal. No Hermes CLI.
    /// </summary>
    private async Task<bool> TryHandleMt5TerminalRefreshLocalAsync(string userPayload, string projectName)
    {
        if (!Mt5TerminalTradeRouter.IsMt5TerminalProject(projectName))
        {
            return false;
        }

        if (!Mt5TerminalTradeRouter.LooksLikeRefreshRequest(userPayload))
        {
            return false;
        }

        _logService.LogInfo("[mt5-refresh] local (no Hermes CLI)");
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var exec = await _mt5TerminalIpc
                .ExecuteAsync(
                    new Mt5TerminalRouteCommand { Action = "snapshot", Id = id },
                    Projects.SelectedProject?.WindowsPath,
                    TimeSpan.FromSeconds(45))
                .ConfigureAwait(true);

            var pub = await _hwtStatusPublisher.PublishNowAsync().ConfigureAwait(true);
            var ok = exec.Ok && pub.Ok;
            AppendTerminal(
                $"[mt5-refresh] ipc ok={exec.Ok} publish ok={pub.Ok} {pub.Message}",
                isError: !ok);

            if (ok)
            {
                PostMt5TerminalLocalReply(
                    projectName,
                    """
                    [info]
                    Терминал обновлён → RemoteTerminal.

                    [Voice]
                    {"ru":"Терминал обновлён."}
                    [/Voice]
                    """.Trim());
            }
            else
            {
                var detail = string.IsNullOrWhiteSpace(pub.Message) ? exec.Error ?? exec.Message : pub.Message;
                PostMt5TerminalLocalReply(
                    projectName,
                    "[info]\nRefresh: " + detail
                    + "\n\n[Voice]\n{\"ru\":\"Обновление не удалось.\"}\n[/Voice]");
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarn("[mt5-refresh] " + ex.Message);
            PostMt5TerminalLocalReply(
                projectName,
                "[info]\nRefresh: " + ex.Message + "\n\n[Voice]\n{\"ru\":\"Ошибка обновления.\"}\n[/Voice]");
            AppendTerminal("[mt5-refresh] " + ex.Message, isError: true);
        }

        return true;
    }

    private void PostMt5TerminalLocalReply(string projectName, string rawWithVoice)
    {
        var display = HermesReplySplit.ForChatDisplay(rawWithVoice);
        DispatchToUi(() => Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = display }));
        _chatLogService.AppendMessage(projectName, "Hermes", display);
        RequestChatScrollToBottom();
        _ = PublishAssistantTurnToSupabaseIfPossibleAsync(rawWithVoice);
        _ = TrySaveHistoryAfterTurnAsync(projectName);
    }

    /// <summary>
    /// Mt5Terminal: agent is a whitelist router (JSON in stdout). WPF executes via HermesWpfTerminal IPC.
    /// </summary>
    private async Task<string> ResolveMt5TerminalRouterChatAsync(
        string projectName,
        HermesExecutionResult result,
        string? userMessage = null)
    {
        if (!Mt5TerminalTradeRouter.IsMt5TerminalProject(projectName))
        {
            return string.IsNullOrWhiteSpace(result.EffectiveDisplayText)
                ? "(пустой ответ)"
                : result.EffectiveDisplayText;
        }

        var parseSource = string.IsNullOrWhiteSpace(result.DisplayText)
            ? result.CombinedText
            : result.DisplayText;
        var route = Mt5TerminalTradeRouter.TryParseFromAgentOutput(parseSource);
        if (route is null)
        {
            // Clear chart-screenshot intent even if agent omitted JSON / used old session rules.
            if (Mt5TerminalTradeRouter.LooksLikeChartScreenshotRequest(userMessage))
            {
                route = new Mt5TerminalRouteCommand
                {
                    Action = "screenshot",
                    Id = Guid.NewGuid().ToString("N"),
                };
                _logService.LogInfo("[mt5-router] no JSON — forced screenshot from user intent");
            }
            else if (Mt5TerminalTradeRouter.LooksLikeRefreshRequest(userMessage))
            {
                route = new Mt5TerminalRouteCommand
                {
                    Action = "refresh",
                    Id = Guid.NewGuid().ToString("N"),
                };
                _logService.LogInfo("[mt5-router] no JSON — forced refresh from user intent");
            }
            else if (Mt5TerminalTradeRouter.LooksLikeStatusOrBalanceRequest(userMessage))
            {
                route = new Mt5TerminalRouteCommand
                {
                    Action = "snapshot",
                    Id = Guid.NewGuid().ToString("N"),
                };
                _logService.LogInfo("[mt5-router] no JSON — forced snapshot from status/balance intent");
            }
            else
            {
                _logService.LogWarn("[mt5-router] no whitelist JSON in agent stdout — execution skipped");
                return Mt5TerminalTradeRouter.FormatMissingJsonChat();
            }
        }
        else
        {
            route = Mt5TerminalTradeRouter.CorrectRouteForUserIntent(route, userMessage);
        }

        if (route.IsUnsupported)
        {
            _logService.LogInfo($"[mt5-router] unsupported: {route.Reason}");
            return Mt5TerminalTradeRouter.FormatUnsupportedChat(route);
        }

        var projectPath = Projects.SelectedProject?.WindowsPath;
        _logService.LogInfo($"[mt5-router] execute action={route.Action} id={route.Id}");
        try
        {
            var timeout = string.Equals(route.Action, "screenshot", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(60)
                : TimeSpan.FromSeconds(45);

            // refresh → IPC snapshot (обновить status.json) + publish to RemoteTerminal
            var ipcRoute = string.Equals(route.Action, "refresh", StringComparison.OrdinalIgnoreCase)
                ? new Mt5TerminalRouteCommand { Action = "snapshot", Id = route.Id }
                : route;

            var exec = await _mt5TerminalIpc
                .ExecuteAsync(ipcRoute, projectPath, timeout)
                .ConfigureAwait(true);

            if (string.Equals(route.Action, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                var pub = await _hwtStatusPublisher.PublishNowAsync().ConfigureAwait(true);
                exec.Ok = exec.Ok && pub.Ok;
                exec.Message = string.IsNullOrWhiteSpace(exec.Message)
                    ? pub.Message
                    : exec.Message + "\n" + pub.Message;
                if (!pub.Ok && string.IsNullOrWhiteSpace(exec.Error))
                {
                    exec.Error = pub.Message;
                }

                AppendTerminal(
                    $"[mt5-ipc] refresh publish ok={pub.Ok} {pub.Message}",
                    isError: !pub.Ok);
            }

            if (string.Equals(route.Action, "screenshot", StringComparison.OrdinalIgnoreCase)
                && exec.Ok
                && !string.IsNullOrWhiteSpace(exec.ScreenshotPath))
            {
                try
                {
                    if (_supabaseRelay is { IsConnected: true })
                    {
                        var shot = await _supabaseRelay
                            .PublishHwtScreenshotAsync(exec.ScreenshotPath!)
                            .ConfigureAwait(true);
                        if (shot.Ok)
                        {
                            _lastHwtScreenshotPath = exec.ScreenshotPath;
                        }

                        AppendTerminal(
                            $"[hwt-screenshot] remote ok={shot.Ok} {shot.Message}",
                            isError: !shot.Ok);
                        if (!string.IsNullOrWhiteSpace(shot.Message))
                        {
                            exec.Message = string.IsNullOrWhiteSpace(exec.Message)
                                ? shot.Message
                                : exec.Message + "\n" + shot.Message;
                        }
                    }
                    else
                    {
                        AppendTerminal("[hwt-screenshot] relay offline — RemoteTerminal не уведомлён", isError: true);
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogWarn("[hwt-screenshot] " + ex.Message);
                    AppendTerminal("[hwt-screenshot] " + ex.Message, isError: true);
                }
            }

            AppendTerminal(
                $"[mt5-ipc] {route.Action} ok={exec.Ok}" + (string.IsNullOrWhiteSpace(exec.Error) ? string.Empty : " " + exec.Error),
                isError: !exec.Ok);
            return Mt5TerminalIpcClient.FormatChatMessage(route, exec, userMessage);
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[mt5-ipc] {ex.Message}");
            return $"Задача: {route.Action}\nИсполнение: FAIL\n{ex.Message}";
        }
    }

    private static bool ShouldInjectDesktopContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DesktopWindowFocusTriggers.Matches(text) || DesktopScreenCaptureTriggers.Matches(text))
        {
            return true;
        }

        var t = text.ToLowerInvariant();
        return t.Contains("окн", StringComparison.Ordinal)
               || t.Contains("экран", StringComparison.Ordinal)
               || t.Contains("скрин", StringComparison.Ordinal)
               || t.Contains("screen", StringComparison.Ordinal)
               || t.Contains("клик", StringComparison.Ordinal)
               || t.Contains("кноп", StringComparison.Ordinal)
               || t.Contains("мыш", StringComparison.Ordinal)
               || t.Contains("window", StringComparison.Ordinal)
               || t.Contains("click", StringComparison.Ordinal);
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

    /// <summary>Outbound-only: add instruction so relay/test numeric codes are echoed verbatim in the model reply.</summary>
    private static bool LooksLikeVerificationTestMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length >= 4 && trimmed.All(char.IsAsciiDigit))
        {
            return true;
        }

        if (!FourPlusDigitRun.IsMatch(trimmed))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        foreach (var k in VerificationTestKeywords)
        {
            if (lower.Contains(k, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    public MiniAssistantViewModel InAppAssistant { get; }

    public event Action? ChatScrollToBottomRequested;

    public void RequestChatScrollToBottom() => ChatScrollToBottomRequested?.Invoke();

    private void NotifyHermesReplyArrived(string projectName, string replyText, bool isError = false)
    {
        try
        {
            if (isError)
            {
                // Detailed popup: raw error + explanation + fix steps (OpenRouter limit, WSL, …).
                HermesErrorHelpWindow.ShowForError(
                    Application.Current?.MainWindow,
                    replyText);
                return;
            }

            HermesReplyNotifyService.Notify(
                Application.Current?.MainWindow,
                projectName,
                replyText,
                Settings.NotifyOnHermesReply,
                isError: false);
        }
        catch (Exception ex)
        {
            _logService.LogWarn("[notify] " + ex.Message);
        }
    }

    private void NotifyHermesCliFailure(string projectName, HermesExecutionResult result, string hint)
    {
        var errBubble = $"Ошибка CLI (exit {result.ExitCode}): {hint}";
        try
        {
            HermesErrorHelpWindow.ShowForError(
                Application.Current?.MainWindow,
                hint,
                result.ExitCode);
        }
        catch (Exception ex)
        {
            _logService.LogWarn("[notify] error-help: " + ex.Message);
            NotifyHermesReplyArrived(projectName, errBubble, isError: true);
        }
    }
    public ProjectViewModel Projects { get; }
    public WslMemoryViewModel WslMemory { get; }
    public HermesSettings Settings { get; }
    public ObservableCollection<string> SessionHistoryTitles { get; } = [];

    public ICommand AddProjectCommand { get; }
    public ICommand BrowseProjectFolderCommand { get; }
    public ICommand RenameProjectCommand { get; }
    public ICommand MoveProjectUpCommand { get; }
    public ICommand MoveProjectDownCommand { get; }
    public ICommand OpenProjectManagerWindowCommand { get; }
    public ICommand OpenMiniConsoleCommand { get; }
    public ICommand OpenMainConsoleCommand { get; }
    public ICommand OpenProjectManagerDashboardCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand AttachChatFileCommand { get; }
    public ICommand AttachChatScreenshotCommand { get; }
    public ICommand ClearChatAttachmentsCommand { get; }
    public ICommand RemoveChatAttachmentCommand { get; }
    public ICommand RetryLastMessageCommand { get; }
    public ICommand GatewayRunCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand ResetWebhookCommand { get; }
    public ICommand AnalyzeCodeCommand { get; }

    /// <summary>Human-language test commands → Mt5Terminal agent chat (same as typing).</summary>
    public ICommand OpenMt5TerminalTestCommandsCommand { get; }

    /// <summary>Quote-relative market/pending test order cards → Mt5Terminal IPC.</summary>
    public ICommand OpenMt5TerminalTestOrdersCommand { get; }

    public ICommand ReconnectCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public ICommand ToggleSupabaseRelayCommand { get; }

    public ICommand SmokeTestMouseSkillCommand { get; }

    public ICommand CaptureDesktopScreenshotCommand { get; }

    /// <summary>Opens the memory editor — assign handler via <c>AttachSaveExperienceOpener</c> from the main window.</summary>
    public ICommand SaveExperienceCommand { get; }

    /// <summary>Clears Hermes CLI <c>--resume</c> id for the selected project.</summary>
    public ICommand ResetCliSessionCommand { get; }

    public ICommand ShowWhatsAppWebWindowCommand { get; }

    public ICommand RestartWhatsAppWebCommand { get; }

    public ICommand RunWhatsAppParseProbeCommand { get; }

    public ICommand ExportEnglishTutorProgressCommand { get; }

    public ICommand LaunchBinanceDemoSpotCommand { get; }

    public ICommand LaunchBinanceDemoFuturesCommand { get; }

    public ICommand StopFlashcardsCommand { get; }

    public ICommand SubmitReniWaterCommand { get; }
    public ICommand AckReniWaterCommand { get; }
    public ICommand LoginReniWaterCommand { get; }

    public ICommand ViewReniWaterScreenshotCommand { get; }

    public ICommand CheckReniWaterSessionCommand { get; }

    public AgentTaskSchedulerService AgentScheduler => _agentScheduler;

    public PortfolioStoreService PortfolioStore => _portfolioStore;

    public AgentActivityBus ActivityBus => _activityBus;

    public bool IsProjectManagerWorkspace =>
        Projects.SelectedProject is { Name: var n }
        && n.Equals("ProjectManager", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dashboard / Run Now: inject reminder into the owning project chat.</summary>
    public Task RunSchedulerTaskNowAsync(AgentScheduledTask task)
    {
        if (task.Status is AgentTaskStatus.Completed or AgentTaskStatus.Cancelled)
        {
            AppendTerminal($"[scheduler] Задача {task.Id} уже закрыта — Run Now пропущен.");
            return Task.CompletedTask;
        }

        return DispatchSchedulerTaskAsync(task, forceNow: true);
    }

    private async Task DispatchSchedulerTaskAsync(AgentScheduledTask task, bool forceNow = false)
    {
        try
        {
            if (forceNow
                && task.Status is not AgentTaskStatus.Completed and not AgentTaskStatus.Cancelled)
            {
                // Ensure timer won't also fire; status Fired for overdue/Run Now.
                _agentScheduler.MarkFired(task.Id);
            }

            var project = Projects.Projects.FirstOrDefault(p =>
                string.Equals(p.Name, task.ProjectName, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                _logService.LogWarn(
                    $"[scheduler] project «{task.ProjectName}» not loaded; cannot dispatch id={task.Id}");
                AppendTerminal(
                    $"[scheduler] Проект «{task.ProjectName}» не в списке — напоминание id={task.Id} не отправлено.",
                    isError: true);
                return;
            }

            if (!ReferenceEquals(Projects.SelectedProject, project))
            {
                Projects.SelectedProject = project;
                await EnsureSelectedProjectHistoryLoadedAsync().ConfigureAwait(true);
            }

            var payload =
                $"[scheduler reminder] id={task.Id}\n"
                + $"Задача: {task.Title}\n\n"
                + $"{task.Command}\n\n"
                + "Выполни задачу. После успеха или если неактуально — удали из планировщика:\n"
                + $"{{\"skill\":\"wpf_local\",\"action\":\"scheduler_complete\",\"task_id\":\"{task.Id}\"}}";

            _logService.LogInfo($"[scheduler] inject → {task.ProjectName} id={task.Id}");
            AppendTerminal($"[scheduler] Напоминание → {task.ProjectName}: {task.Title}");

            // Wait if another Hermes turn is in progress (simple backoff).
            for (var i = 0; i < 60 && _isBusy; i++)
            {
                await Task.Delay(1000).ConfigureAwait(true);
            }

            if (_isBusy)
            {
                _logService.LogWarn($"[scheduler] still busy; drop inject id={task.Id}");
                AppendTerminal($"[scheduler] Агент занят — напоминание id={task.Id} отложено (остаётся Fired).", isError: true);
                return;
            }

            await SendMessageTextAsync(payload).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[scheduler] dispatch error id={task.Id}: {ex.Message}");
            AppendTerminal($"[scheduler] Ошибка доставки: {ex.Message}", isError: true);
        }
    }

    /// <summary>Detect scheduled work that should have run while Hermes.Wpf / PC was off.</summary>
    public IReadOnlyList<MissedScheduledTaskInfo> DetectMissedScheduledTasks()
    {
        var detector = new MissedScheduledTaskService(_logService, () => Settings);
        return detector.DetectMissed();
    }

    /// <summary>Run Now from missed-task popup (local Playwright path, not CLI).</summary>
    public async Task<(bool Ok, string Message)> RunMissedScheduledTaskAsync(MissedScheduledTaskInfo task)
    {
        _logService.LogInfo($"[missed-tasks] Run Now id={task.Id} kind={task.Kind}");
        switch (task.Kind)
        {
            case MissedTaskKind.ReniWaterMonthly:
            case MissedTaskKind.ReniWaterOnce:
            {
                _reniWaterBusy = true;
                try
                {
                    var project = Projects.SelectedProject?.Name ?? "Utilities";
                    var result = await _wpfLocalExecutor
                        .ExecuteAsync(
                            new WpfLocalIntent { Action = "reni_water_submit" },
                            project,
                            $"Run Now (missed): {task.Title}",
                            "missed-task-popup",
                            CancellationToken.None)
                        .ConfigureAwait(true);

                    if (result.Ok)
                    {
                        Settings.ReniWaterLastMonthlyRunKey =
                            DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                        await _settingsService.SaveAsync(Settings).ConfigureAwait(true);
                    }

                    var line = result.Ok
                        ? $"✅ {result.UserMessage}"
                        : $"⚠ {result.UserMessage}";
                    Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = line });
                    if (!string.IsNullOrWhiteSpace(result.ScreenshotPath))
                    {
                        Chat.Messages.Add(new ChatMessage
                        {
                            Role = "Hermes",
                            Text = $"Screenshot: {result.ScreenshotPath}",
                            ImagePath = result.ScreenshotPath,
                        });
                    }

                    return (result.Ok, result.UserMessage);
                }
                finally
                {
                    _reniWaterBusy = false;
                    CommandManager.InvalidateRequerySuggested();
                }
            }
            default:
                return (false, $"Неизвестная задача: {task.Id}");
        }
    }

    public bool CanViewReniWaterScreenshot
    {
        get => _canViewReniWaterScreenshot;
        private set => SetProperty(ref _canViewReniWaterScreenshot, value);
    }

    public bool IsReniWaterStatusBarVisible
    {
        get => _isReniWaterStatusBarVisible;
        private set => SetProperty(ref _isReniWaterStatusBarVisible, value);
    }

    public string ReniWaterStatusText
    {
        get => _reniWaterStatusText;
        private set => SetProperty(ref _reniWaterStatusText, value);
    }

    /// <summary>Detach timers before Supabase shutdown (main window closing).</summary>
    public void ShutdownFlashcardSkillBeforeRelay()
    {
        _remoteLogBridge.Dispose();
        _hwtStatusPublisher.Dispose();
        _flashcardSkill.Dispose();
        _agentScheduler.Dispose();
        _reniWaterPollTimer.Stop();
        _mt5AgentChatInjectTimer.Stop();
        try { _mt5TestCommandsWindow?.Close(); } catch { /* ignore */ }
        _mt5TestCommandsWindow = null;
        try { _mt5TestOrdersWindow?.Close(); } catch { /* ignore */ }
        _mt5TestOrdersWindow = null;
    }

    public bool IsFlashcardStatusBarVisible
    {
        get => _isFlashcardStatusBarVisible;
        private set => SetProperty(ref _isFlashcardStatusBarVisible, value);
    }

    public string FlashcardStatusText
    {
        get => _flashcardStatusText;
        private set => SetProperty(ref _flashcardStatusText, value);
    }

    /// <summary>When true, flashcard indicator uses a subtle pulse in the chat UI.</summary>
    public bool FlashcardDotPulse
    {
        get => _flashcardDotPulse;
        private set => SetProperty(ref _flashcardDotPulse, value);
    }

    public void AttachSaveExperienceOpener(Action opener) =>
        _saveExperienceOpener = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachChatWindowOpener(Action opener) =>
        _openChatWindow = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachProjectManagerWindowOpener(Action opener) =>
        _openProjectManagerWindow = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachMiniConsoleOpener(Action opener) =>
        _openMiniConsole = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachMainConsoleOpener(Action opener) =>
        _openMainConsole = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachProjectManagerDashboardOpener(Action opener) =>
        _openProjectManagerDashboard = opener ?? throw new ArgumentNullException(nameof(opener));

    public void AttachProjectRelatedWindowOpener(Action<HermesProject> opener) =>
        _openProjectRelatedWindow = opener ?? throw new ArgumentNullException(nameof(opener));

    public void RequestOpenChatWindow() => _openChatWindow?.Invoke();

    public void RequestOpenProjectManagerWindow() => _openProjectManagerWindow?.Invoke();

    public void RequestOpenMiniConsole() => _openMiniConsole?.Invoke();

    public void RequestOpenMainConsole() => _openMainConsole?.Invoke();

    public void RequestOpenProjectManagerDashboard() => _openProjectManagerDashboard?.Invoke();

    public void ShowProjectRelatedWindow(HermesProject project) =>
        _openProjectRelatedWindow?.Invoke(project);

    public ProjectUiMeta? GetProjectUiMeta(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
        {
            return null;
        }

        Settings.ProjectUiMetaByPath ??= new Dictionary<string, ProjectUiMeta>(StringComparer.OrdinalIgnoreCase);
        return Settings.ProjectUiMetaByPath.TryGetValue(windowsPath.Trim(), out var meta) ? meta : null;
    }

    public void SetProjectAvatarFromDialog(HermesProject project)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Аватар проекта",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|Все файлы|*.*",
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var stored = ProjectAvatarStore.ImportAvatar(dlg.FileName, project.WindowsPath);
            Settings.ProjectUiMetaByPath ??= new Dictionary<string, ProjectUiMeta>(StringComparer.OrdinalIgnoreCase);
            if (!Settings.ProjectUiMetaByPath.TryGetValue(project.WindowsPath, out var meta) || meta is null)
            {
                meta = new ProjectUiMeta();
                Settings.ProjectUiMetaByPath[project.WindowsPath] = meta;
            }

            meta.AvatarPath = stored;
            _ = PersistSettingsQuietAsync();
            AppendTerminal($"[project] Аватар сохранён: {project.Name}");
        }
        catch (Exception ex)
        {
            AppendTerminal("[project] Аватар: " + ex.Message, isError: true);
        }
    }

    public void LaunchRelatedApp(ProjectRelatedAppInfo app)
    {
        if (string.Equals(app.Id, "wordpress-gallery", StringComparison.OrdinalIgnoreCase))
        {
            _openWordPressGallery?.Invoke();
            return;
        }

        if (string.Equals(app.Id, "binance-futures", StringComparison.OrdinalIgnoreCase))
        {
            LaunchBinanceDemoFuturesManual();
            return;
        }

        if (string.Equals(app.Id, "binance-spot", StringComparison.OrdinalIgnoreCase))
        {
            LaunchBinanceDemoSpotManual();
            return;
        }

        if (string.Equals(app.Id, "command-center", StringComparison.OrdinalIgnoreCase))
        {
            AppendTerminal("[project] Command Center уже открыт.");
            return;
        }

        var exe = ResolveRelatedAppExe(app);
        if (exe is null)
        {
            AppendTerminal($"[project] Не найден exe для «{app.Title}». Пересоберите решение.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            });
            AppendTerminal($"[project] Запущено: {Path.GetFileName(exe)}");
        }
        catch (Exception ex)
        {
            AppendTerminal($"[project] Запуск «{app.Title}»: {ex.Message}", isError: true);
        }
    }

    private static string? ResolveRelatedAppExe(ProjectRelatedAppInfo app)
    {
        if (!string.IsNullOrWhiteSpace(app.ExeFileName))
        {
            var beside = Path.Combine(AppContext.BaseDirectory, app.ExeFileName);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        if (app.DevRelativeExePaths is { Count: > 0 })
        {
            var root = FindRepoRoot();
            if (root is not null)
            {
                foreach (var rel in app.DevRelativeExePaths)
                {
                    var full = Path.GetFullPath(Path.Combine(root, rel));
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Hermes.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, "Hermes.Wpf")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public void AttachWordPressGalleryOpener(Action opener) =>
        _openWordPressGallery = opener ?? throw new ArgumentNullException(nameof(opener));

    public MemoryDraft? GetLastExperienceDraft() => _lastExperienceDraft;

    private void ExportEnglishTutorProgress()
    {
        var memoryRoot = (_externalBrain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
        {
            AppendTerminal("[english-tutor] export failed: External Brain path not set or missing.", isError: true);
            return;
        }

        var local = EnglishTutorObsidianExporter.LocalStorePath();
        var result = _englishTutorExporter.ExportFromLocalStore(memoryRoot, local);
        AppendTerminal("[english-tutor] " + result.Message, isError: !result.Success);
    }

    private void FlashcardSkill_OnStatusChanged(object? sender, FlashcardStatus e)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null || app.Dispatcher.CheckAccess())
        {
            RefreshFlashcardStatusUi();
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        _ = app.Dispatcher.InvokeAsync(() =>
        {
            RefreshFlashcardStatusUi();
            CommandManager.InvalidateRequerySuggested();
        }, DispatcherPriority.Normal);
    }

    private void FlashcardSkill_OnDelayTick(object? sender, EventArgs e) =>
        FlashcardSkill_OnStatusChanged(sender, FlashcardStatus.Idle);

    private void RefreshFlashcardStatusUi()
    {
        switch (_flashcardSkill.Status)
        {
            case FlashcardStatus.Idle:
                IsFlashcardStatusBarVisible = false;
                FlashcardDotPulse = false;
                FlashcardStatusText = string.Empty;
                break;

            case FlashcardStatus.WaitingToStart:
                IsFlashcardStatusBarVisible = true;
                FlashcardDotPulse = false;
                var mins = 0;
                if (_flashcardSkill.ScheduledGenerationUtc is { } target)
                {
                    var rem = target - DateTimeOffset.UtcNow;
                    mins = rem <= TimeSpan.Zero ? 0 : Math.Max(1, (int)Math.Ceiling(rem.TotalMinutes));
                }

                FlashcardStatusText =
                    $"🃏 Flashcards: запуск через ~{mins} мин  •  тема: {_flashcardSkill.CurrentTopic}";
                break;

            case FlashcardStatus.Generating:
                IsFlashcardStatusBarVisible = true;
                FlashcardDotPulse = true;
                FlashcardStatusText =
                    $"🃏 Flashcards: активно  •  тема: {_flashcardSkill.CurrentTopic}  •  каждые {_flashcardSkill.CurrentIntervalMinutes} мин";
                break;

            case FlashcardStatus.Stopped:
            default:
                break;
        }

        RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
        RaisePropertyChanged(nameof(EnglishTutorStatusRibbonText));
        RaisePropertyChanged(nameof(IsTradingModeStatusVisible));
        RaisePropertyChanged(nameof(TradingModeStatusRibbonText));
        RefreshChatModeStatusUi();
        _ = PublishSessionContextToSupabaseIfChangedAsync("flashcards-status");
    }

    private void StopFlashcardsInternal() => _flashcardSkill.Stop();

    public async Task OnChatWindowOpenedAsync()
    {
        RefreshChatModeStatusUi();

        // Trading terminals are for trading mode only — not in default agent mode.
        if (Settings.TradingModeEnabled)
        {
            if (_futuresBridge.IsActiveForSession && Settings.FuturesTerminalAutoLaunch)
            {
                _ = _futuresBridge.EnsureTerminalReadyAsync(force: true);
                _logService.LogInfo("[futures-bridge] auto-launch on chat open (trading mode)");
            }

            if (_spotBridge.IsActiveForSession && Settings.SpotTerminalIntegrationEnabled && Settings.SpotTerminalAutoLaunch)
            {
                _ = _spotBridge.EnsureTerminalReadyAsync(force: true);
                _logService.LogInfo("[spot-bridge] auto-launch on chat open (trading mode)");
            }
        }

        if (_startupModeNoticePending && Projects.SelectedProject is { Name: var projectName })
        {
            _startupModeNoticePending = false;
            SyncChatModeStatusBubble(persistHistory: false);
            _ = PublishSessionContextToSupabaseIfChangedAsync("chat-opened");
        }
    }

    private void LaunchBinanceDemoFuturesManual()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Hermes.BinanceDemoFuturesTerminal.exe");
        if (!File.Exists(exe))
        {
            AppendTerminal("[binance-futures] Hermes.BinanceDemoFuturesTerminal.exe не найден — пересоберите Hermes.Wpf.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
            });
            AppendTerminal("[binance-futures] Запущен Hermes.BinanceDemoFuturesTerminal.exe.");
            _logService.LogInfo($"[binance-futures] launched {exe}");
        }
        catch (Exception ex)
        {
            AppendTerminal($"[binance-futures] Ошибка запуска: {ex.Message}", isError: true);
            _logService.LogError($"[binance-futures] launch failed: {ex.Message}");
        }
    }

    private void LaunchBinanceDemoSpotManual()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Hermes.BinanceDemoSpotTerminal.exe");
        if (!File.Exists(exe))
        {
            AppendTerminal("[binance-demo] Hermes.BinanceDemoSpotTerminal.exe не найден — пересоберите Hermes.Wpf.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
            });
            AppendTerminal("[binance-demo] Запущен Hermes.BinanceDemoSpotTerminal.exe.");
            _logService.LogInfo($"[binance-demo] launched {exe}");
        }
        catch (Exception ex)
        {
            AppendTerminal($"[binance-demo] Ошибка запуска: {ex.Message}", isError: true);
            _logService.LogError($"[binance-demo] launch failed: {ex.Message}");
        }
    }

    private void LaunchSpotTerminalManual() => _ = LaunchSpotTerminalManualAsync();

    private async Task LaunchSpotTerminalManualAsync()
    {
        if (!_spotBridge.IsActiveForSession)
        {
            AppendTerminal("[spot] Включите Spot Terminal в Settings или режим трейдинга.", isError: true);
            return;
        }

        var ready = await _spotBridge.EnsureTerminalReadyAsync(force: true).ConfigureAwait(true);
        var exePath = Path.Combine(AppContext.BaseDirectory, "Hermes.SpotTerminal.exe");
        var depsOk = File.Exists(Path.Combine(AppContext.BaseDirectory, "Hermes.SpotTerminal.dll"));
        AppendTerminal(
            ready
                ? "[spot] Hermes.SpotTerminal готов (heartbeat ok)."
                : !depsOk
                    ? "[spot] Hermes.SpotTerminal.exe без зависимостей в папке Hermes.Wpf. Пересоберите решение (Build Hermes.Wpf)."
                    : !File.Exists(exePath)
                        ? "[spot] Hermes.SpotTerminal.exe не найден — укажите SpotTerminalExePath в Settings."
                        : "[spot] Процесс запущен, но heartbeat не появился — откройте окно SpotTerminal вручную.",
            isError: !ready);
        _logService.LogInfo($"[spot-bridge] manual launch ready={ready}");
    }

    private void EnsureSpotTerminalRunning(bool force = true)
    {
        if (!_spotBridge.IsActiveForSession)
        {
            return;
        }

        _spotBridge.EnsureTerminalRunning(force);
    }

    private void EnsureFuturesTerminalRunning(bool force = true)
    {
        if (!_futuresBridge.IsActiveForSession)
        {
            return;
        }

        _futuresBridge.EnsureTerminalRunning(force);
    }

    private static bool ShouldRouteTradingToFutures(string? market, string action)
    {
        if (!string.IsNullOrWhiteSpace(market))
        {
            if (market.Equals("spot", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (market.Equals("futures", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return action.ToLowerInvariant() switch
        {
            "close_position" or "close_all_positions" or "set_leverage" => true,
            _ => true,
        };
    }

    private void RefreshChatModeStatusUi()
    {
        if (_flashcardSkill is null)
        {
            return;
        }

        ChatModeStatusText = HermesChatModeResolver.BuildChatStatusLine(
            Projects.SelectedProject?.Name,
            Settings,
            _flashcardSkill.Status);
        RaisePropertyChanged(nameof(IsChatModeStatusVisible));
    }

    private string BuildInAppAssistantLiveContext()
    {
        var project = Projects.SelectedProject?.Name ?? "(none)";
        var modeId = HermesChatModeResolver.ResolveModeId(Settings, _flashcardSkill.Status);
        var modeRu = HermesChatModeResolver.ResolveModeDisplayRu(modeId);
        var role = RoleManager.DisplayName(_roleManager.CurrentRole);
        var conn = ConnectionStatusMessage;
        var supabase = Settings.SupabaseRelayEnabled ? "on" : "off";
        var agentPaused = Settings.HermesAgentPaused ? "paused" : "active";
        var tradingBridge = _spotBridge.IsActiveForSession
            ? (_spotBridge.IsTerminalAlive() ? "SpotTerminal alive" : "SpotTerminal not running")
            : "disabled";
        var flashcards = _flashcardSkill.Status.ToString();

        return $"""
            Application: Hermes Command Center (Hermes.Wpf)
            Selected project: {project}
            Chat mode: {modeRu} ({modeId})
            Assistant mode (main chat OpenRouter): {(Settings.AssistantModeEnabled ? "on" : "off")}
            Agent role: {role}
            Hermes WSL connection: {conn}
            Main agent chat: {agentPaused}
            Supabase relay: {supabase}
            Flashcards skill: {flashcards}
            Trading platform bridge: {tradingBridge}
            Active UI: main window tabs (Терминал | Память WSL | Навыки); full agent chat in separate Chat window
            Status line: {ChatModeStatusText}
            """;
    }

    private string BuildSessionContextFingerprint()
    {
        var project = Projects.SelectedProject?.Name?.Trim() ?? string.Empty;
        var mode = HermesChatModeResolver.ResolveModeId(Settings, _flashcardSkill.Status);
        return $"{project}|{mode}";
    }

    private async Task PublishSessionContextToSupabaseIfChangedAsync(string reason)
    {
        if (!Settings.SupabaseRelayEnabled)
        {
            return;
        }

        await _sessionPublishLock.WaitAsync().ConfigureAwait(true);
        try
        {
            var fingerprint = BuildSessionContextFingerprint();
            if (string.Equals(_lastPublishedSessionFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            await EnsureSupabaseRelayReadyForPublishAsync().ConfigureAwait(true);
            if (_supabaseRelay is not { IsConnected: true })
            {
                return;
            }

            var project = Projects.SelectedProject?.Name;
            if (string.IsNullOrWhiteSpace(project))
            {
                return;
            }

            var modeId = HermesChatModeResolver.ResolveModeId(Settings, _flashcardSkill.Status);
            var content = HermesWpfSessionContextPayload.BuildSupabaseContent(project, modeId);
            var label = CanonicalHermesSenderName();

            try
            {
                await _supabaseRelay.InsertAssistantRowAsync(
                        label,
                        CanonicalHermesOutboundRecipient(),
                        content,
                        CancellationToken.None,
                        logPublish: false)
                    .ConfigureAwait(true);
                _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, content);
                _lastPublishedSessionFingerprint = fingerprint;
                _logService.LogInfo(
                    $"[supabase] session voice ({reason}): project={project}, mode={modeId}");
            }
            catch (Exception ex)
            {
                _logService.LogWarn($"[supabase] session voice publish failed: {ex.Message}");
            }
        }
        finally
        {
            _sessionPublishLock.Release();
        }
    }

    private async Task<string?> GenerateFlashcardJsonViaHermesAsync(
        string topic,
        IReadOnlyList<string> alreadySentEnglish,
        bool retryStricterPrompt,
        CancellationToken cancellationToken)
    {
        if (Projects.SelectedProject is null)
        {
            _logService.LogWarn("[flashcards] нет выбранного проекта — генерация пропущена.");
            return null;
        }

        var wslPath = ResolveHermesWslWorkingDirectory(Projects.SelectedProject.WindowsPath);
        var payload = FlashcardHermesGenerationPrompt.BuildUserPayload(topic, alreadySentEnglish, retryStricterPrompt);
        var timeout = ClampChatTimeout(Settings.ChatTimeoutSeconds);
        var result = await _hermesService.SendMessageAsync(payload, wslPath, Settings, timeout).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            _logService.LogWarn($"[flashcards] Hermes exit={result.ExitCode}");
            return null;
        }

        return result.CombinedText;
    }

    private async Task<bool> PublishFlashcardJsonToSupabaseAsync(string json, CancellationToken cancellationToken)
    {
        if (!Settings.SupabaseRelayEnabled)
        {
            _logService.LogWarn("[flashcards] Supabase relay выключен — карточка не опубликована.");
            return false;
        }

        try
        {
            await EnsureSupabaseRelayReadyForPublishAsync();
            if (_supabaseRelay is not { IsConnected: true })
            {
                _logService.LogError("[flashcards] Relay не подключён — карточка не отправлена.");
                return false;
            }

            var label = CanonicalHermesSenderName();
            await _supabaseRelay.InsertAssistantRowAsync(label, CanonicalHermesOutboundRecipient(), json, cancellationToken);
            _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, json);
            _logService.LogInfo($"[flashcards] опубликована карточка (chars={json.Length}).");
            return true;
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[flashcards] insert failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Shown above the chat composer while Hermes subprocess is running.</summary>
    public string AgentChatStatusLine
    {
        get => _agentChatStatusLine;
        private set => SetProperty(ref _agentChatStatusLine, value);
    }

    public bool IsAgentChatStatusBarVisible
    {
        get => _isAgentChatStatusBarVisible;
        private set => SetProperty(ref _isAgentChatStatusBarVisible, value);
    }

    /// <summary>Chat header dot: Ready / Waiting / Stalled / Qr / Error / Off.</summary>
    public string WhatsAppIndicatorState => _whatsAppReadiness switch
    {
        WhatsAppMonitorReadiness.Ready => "Ready",
        WhatsAppMonitorReadiness.Stalled => "Stalled",
        WhatsAppMonitorReadiness.QrRequired => "Qr",
        WhatsAppMonitorReadiness.Error => "Error",
        WhatsAppMonitorReadiness.Off => "Off",
        _ => "Waiting",
    };

    public bool IsWhatsAppChatStatusVisible =>
        Settings.WhatsAppWebEnabled && _whatsAppReadiness != WhatsAppMonitorReadiness.Off;

    public string WhatsAppStatusText
    {
        get => _whatsAppStatusText;
        private set => SetProperty(ref _whatsAppStatusText, value);
    }

    /// <summary>Ellipse style in main window: Live / Idle / Off.</summary>
    public string SupabaseIndicatorState =>
        !Settings.SupabaseRelayEnabled ? "Off"
        : _supabaseRelay?.IsConnected == true ? "Live" : "Idle";

    public string SupabaseRelayStatusText =>
        _supabaseRelay?.IsConnected == true ? "Connected" : "Disconnected";

    public string SupabaseRelayToggleCaption =>
        _supabaseRelay?.IsConnected == true ? "Disconnect" : "Connect";

    public string SupabaseRelayToggleHint =>
        _supabaseRelay?.IsConnected == true
            ? "Остановить опрос и локальный клиент Supabase (параметры URL/key сохраняются)."
            : "Подключить relay к Supabase (опрос messages, ответы в общий чат).";

    /// <summary>Short tooltip on the status-bar Supabase control.</summary>
    public string SupabaseRelayToolTipShort =>
        _supabaseRelay?.IsConnected == true
            ? "Relay активен: таблица messages — синхронизация чата с Android и др."
            : "Relay отключён. Нажмите Connect (нужны URL и anon key в Settings).";

    public IReadOnlyList<AgentSkillCard> AgentSkills => _generatedSkillCatalog.AllCards();

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
                if (!string.IsNullOrEmpty(trimmed))
                    _logService.LogWarn($"[project] Skip restore (folder missing): {trimmed}");
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
        var historyPath = _historyService.GetHistoryFilePath(project.Name);
        var history = await _historyService.LoadAsync(project.Name);
        cancellationToken.ThrowIfCancellationRequested();

        Chat.Messages.Clear();

        foreach (var message in history.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Chat.Messages.Add(ChatMessageImageParser.Normalize(message));
        }

        _chatHistoryBoundProject = project.Name;

        _logService.LogInfo(
            $"[history] Loaded {history.Messages.Count} message(s) for «{project.Name}» from {historyPath}" +
            (history.Messages.Count == 0 ? " (empty or new session)" : string.Empty));

        SyncChatModeStatusBubble(persistHistory: false);
        RequestChatScrollToBottom();
    }

    /// <summary>
    /// Before switching projects, write the in-memory chat under the project it belongs to
    /// (avoids losing the last turn when only chat_*.log was appended).
    /// </summary>
    private async Task PersistBoundChatHistoryBeforeSwitchAsync()
    {
        var bound = _chatHistoryBoundProject;
        if (string.IsNullOrWhiteSpace(bound) || Chat.Messages.Count == 0)
        {
            return;
        }

        var next = Projects.SelectedProject?.Name;
        if (!string.IsNullOrWhiteSpace(next)
            && string.Equals(bound, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var snapshot = Chat.Messages.ToList();
            await SaveHistorySnapshotAsync(bound, snapshot).ConfigureAwait(true);
            _logService.LogInfo(
                $"[history] Saved {snapshot.Count} message(s) for «{bound}» before switch" +
                (string.IsNullOrWhiteSpace(next) ? string.Empty : $" → «{next}»"));
        }
        catch (Exception ex)
        {
            _logService.LogError($"[history] Pre-switch save failed ({bound}): {ex.Message}");
        }
    }

    public async Task EnsureSelectedProjectHistoryLoadedAsync()
    {
        if (Projects.SelectedProject is null)
        {
            return;
        }

        if (_pendingHistoryLoad is not null)
        {
            try
            {
                await _pendingHistoryLoad.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Project switched while load was in flight.
            }

            return;
        }

        await LoadProjectHistoryAsync(Projects.SelectedProject).ConfigureAwait(true);
    }

    public async Task SaveCurrentProjectHistoryAsync()
    {
        if (Projects.SelectedProject is not { Name: var projectName })
        {
            return;
        }

        try
        {
            await SaveHistoryAsync(projectName).ConfigureAwait(true);
            _logService.LogInfo(
                $"[history] Saved {Chat.Messages.Count} message(s) for «{projectName}» on shutdown");
        }
        catch (Exception ex)
        {
            _logService.LogError($"[history] Shutdown save failed ({projectName}): {ex.Message}");
        }
    }

    private bool CanExecuteProjectCommand() => !_isBusy && Projects.SelectedProject is not null;

    private bool CanMoveSelectedProject(int delta)
    {
        if (_isBusy || Projects.SelectedProject is null || Projects.Projects.Count < 2)
        {
            return false;
        }

        var idx = Projects.Projects.IndexOf(Projects.SelectedProject);
        if (idx < 0)
        {
            return false;
        }

        var next = idx + delta;
        return next >= 0 && next < Projects.Projects.Count;
    }

    private void MoveSelectedProject(int delta)
    {
        if (!CanMoveSelectedProject(delta))
        {
            return;
        }

        var selected = Projects.SelectedProject!;
        var idx = Projects.Projects.IndexOf(selected);
        var newIndex = idx + delta;
        Projects.Projects.Move(idx, newIndex);
        SnapshotProjectsIntoSettings();
        _ = PersistSettingsQuietAsync();
        CommandManager.InvalidateRequerySuggested();
        AppendTerminal($"[workspaces] «{selected.Name}» → позиция {newIndex + 1}/{Projects.Projects.Count}");
    }

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
                if (status.State == ConnectionState.Connected)
                {
                    _ = _hermesService.WarmUpWslHomeAsync(Settings);
                }
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
        _projectAgentsBootstrap.EnsureProjectHermesArtifacts(project.WindowsPath);
        AppendTerminal($"Project added: {project.Name}");
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RenameSelectedProjectAsync()
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[project] Сначала выберите проект.", isError: true);
            return;
        }

        var selected = Projects.SelectedProject;
        var dlg = new RenameProjectWindow(selected.Name)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (dlg.ShowDialog() != true)
            return;

        var desired = dlg.NewName;
        var sanitized = ProjectRenameService.SanitizeFolderName(desired);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            AppendTerminal("[project] Недопустимое имя.", isError: true);
            return;
        }

        if (!string.Equals(desired.Trim(), sanitized, StringComparison.Ordinal))
        {
            var confirm = MessageBox.Show(
                Application.Current?.MainWindow,
                $"Имя будет сохранено как папка:\n{sanitized}\n\nПродолжить?",
                "Rename project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        _suppressProjectSelectionHandler = true;
        try
        {
            _isBusy = true;
            CommandManager.InvalidateRequerySuggested();

            // Persist chat under old name before moving history file.
            await TrySaveHistoryAfterTurnAsync(selected.Name).ConfigureAwait(true);

            var renamer = new ProjectRenameService(_projectService, _historyService, _cliSessionStore, _logService);
            var result = await Task.Run(() => renamer.Rename(selected, sanitized)).ConfigureAwait(true);

            // Persist paths BEFORE UI mutations — if WPF crashes mid-update, project still restores.
            for (var i = 0; i < Settings.SavedProjectPaths.Count; i++)
            {
                if (string.Equals(Settings.SavedProjectPaths[i], result.OldPath, StringComparison.OrdinalIgnoreCase))
                    Settings.SavedProjectPaths[i] = result.NewPath;
            }

            if (!Settings.SavedProjectPaths.Any(p =>
                    string.Equals(p, result.NewPath, StringComparison.OrdinalIgnoreCase)))
                Settings.SavedProjectPaths.Add(result.NewPath);

            Settings.LastSelectedProjectPath = result.NewPath;
            if (string.Equals(Settings.LastProjectBrowsePath, result.OldPath, StringComparison.OrdinalIgnoreCase))
                Settings.LastProjectBrowsePath = result.NewPath;

            try
            {
                await _settingsService.SaveAsync(Settings).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logService.LogWarn("[settings] save after rename (pre-UI): " + ex.Message);
            }

            _chatLogService.ClearProjectCache(result.OldName);
            _chatLogService.ClearProjectCache(result.NewName);

            // Avoid ObservableCollection indexer replace while SelectedItem is bound (WPF re-entrancy).
            Projects.SelectedProject = null;
            Projects.Projects.Remove(selected);
            if (!Projects.Projects.Any(p =>
                    string.Equals(p.WindowsPath, result.NewPath, StringComparison.OrdinalIgnoreCase)))
                Projects.Projects.Add(result.Project);
            Projects.SelectedProject = result.Project;

            SnapshotProjectsIntoSettings();
            try
            {
                await _settingsService.SaveAsync(Settings).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logService.LogWarn("[settings] save after rename: " + ex.Message);
            }

            _logService.SetActiveProject(result.NewName);
            _projectAgentsBootstrap.EnsureProjectHermesArtifacts(result.NewPath);
            RefreshChatModeStatusUi();

            try
            {
                await LoadProjectHistoryAsync(result.Project).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logService.LogWarn("[project] history after rename: " + ex.Message);
            }

            for (var i = 0; i < SessionHistoryTitles.Count; i++)
            {
                if (SessionHistoryTitles[i].IndexOf(result.OldName, StringComparison.OrdinalIgnoreCase) >= 0)
                    SessionHistoryTitles[i] = SessionHistoryTitles[i].Replace(result.OldName, result.NewName, StringComparison.OrdinalIgnoreCase);
            }

            AppendTerminal($"[project] Renamed: {result.OldName} → {result.NewName}");
            if (!string.IsNullOrWhiteSpace(result.Summary))
                AppendTerminal(result.Summary);
            AppendTerminal("[project] ~/.hermes memories/skills не трогались — контекст сохранён через папку проекта.");
        }
        catch (Exception ex)
        {
            AppendTerminal("[project] Rename failed: " + ex.Message, isError: true);
            _logService.LogError("[project] rename: " + ex.Message);
        }
        finally
        {
            _suppressProjectSelectionHandler = false;
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
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

    private bool CanSendChatMessage() =>
        !string.IsNullOrWhiteSpace(Chat.UserInput) || Chat.HasPendingAttachments;

    private void ClearPendingChatAttachments()
    {
        Chat.ClearPendingAttachments();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemovePendingChatAttachment(ChatAttachment? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        Chat.PendingAttachments.Remove(attachment);
        CommandManager.InvalidateRequerySuggested();
    }

    private void AttachChatFilesFromDialog()
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Выберите проект перед вложением файла.", isError: true);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Прикрепить файлы к чату",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Все файлы|*.*|Изображения|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|Документы|*.pdf;*.txt;*.md;*.docx;*.xlsx;*.csv",
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        AttachChatFilesFromPaths(dlg.FileNames);
    }

    private void AttachChatScreenshot()
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Выберите проект перед скриншотом.", isError: true);
            return;
        }

        try
        {
            var capture = _desktopScreenCapture.CapturePrimaryMonitor();
            var source = !string.IsNullOrWhiteSpace(capture.AnnotatedImagePath) && File.Exists(capture.AnnotatedImagePath)
                ? capture.AnnotatedImagePath
                : capture.ImagePath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                AppendTerminal("[chat] Скриншот не создан.", isError: true);
                return;
            }

            AttachChatFilesFromPaths(new[] { source });
            AppendTerminal($"[chat] Скриншот прикреплён: {Path.GetFileName(source)}");
        }
        catch (Exception ex)
        {
            AppendTerminal("[chat] Скриншот: " + ex.Message, isError: true);
            _logService.LogWarn("[chat-attach] screenshot: " + ex.Message);
        }
    }

    /// <summary>Paste/drop entry point from ChatView.</summary>
    public void AttachChatFilesFromPaths(IEnumerable<string> paths)
    {
        var project = Projects.SelectedProject;
        if (project is null)
        {
            AppendTerminal("[chat] Выберите проект перед вложением.", isError: true);
            return;
        }

        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                if (Chat.PendingAttachments.Count >= 12)
                {
                    AppendTerminal("[chat] Лимит 12 вложений на сообщение.", isError: true);
                    break;
                }

                var att = ChatAttachmentStore.ImportFile(path, project.WindowsPath);
                Chat.PendingAttachments.Add(att);
                added++;
            }
            catch (Exception ex)
            {
                AppendTerminal("[chat] Вложение: " + ex.Message, isError: true);
                _logService.LogWarn("[chat-attach] " + ex.Message);
            }
        }

        if (added > 0)
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Clipboard image paste from ChatView (Ctrl+V).</summary>
    public bool TryAttachClipboardImage()
    {
        var project = Projects.SelectedProject;
        if (project is null || !Clipboard.ContainsImage())
        {
            return false;
        }

        try
        {
            var image = Clipboard.GetImage();
            if (image is null)
            {
                return false;
            }

            if (Chat.PendingAttachments.Count >= 12)
            {
                AppendTerminal("[chat] Лимит 12 вложений на сообщение.", isError: true);
                return true;
            }

            var att = ChatAttachmentStore.ImportBitmapSource(image, project.WindowsPath);
            Chat.PendingAttachments.Add(att);
            CommandManager.InvalidateRequerySuggested();
            AppendTerminal("[chat] Изображение из буфера прикреплено.");
            return true;
        }
        catch (Exception ex)
        {
            AppendTerminal("[chat] Буфер: " + ex.Message, isError: true);
            return false;
        }
    }

    private static string BuildChatAttachmentAgentPayload(
        string userText,
        IReadOnlyList<ChatAttachment> attachments,
        ProjectService projectService)
    {
        var text = (userText ?? string.Empty).Trim();
        if (attachments.Count == 0)
        {
            return text;
        }

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(text))
        {
            lines.Add(text);
            lines.Add(string.Empty);
        }

        lines.Add("Attached files (local paths for tools/vision — WSL):");
        foreach (var a in attachments)
        {
            var wsl = projectService.ConvertToWslPath(a.FilePath);
            var kind = a.IsImage ? "image" : "file";
            lines.Add($"- [{kind}] {a.DisplayName} → {wsl}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildChatAttachmentBubbleText(string userText, IReadOnlyList<ChatAttachment> attachments)
    {
        var text = (userText ?? string.Empty).Trim();
        if (attachments.Count == 0)
        {
            return text;
        }

        var names = string.Join(", ", attachments.Select(a => a.DisplayName));
        var prefix = attachments.Count == 1 ? "📎 " + names : $"📎 {attachments.Count} files: {names}";
        return string.IsNullOrEmpty(text) ? prefix : text + Environment.NewLine + prefix;
    }

    private async Task SendMessageAsync()
    {
        var text = Chat.UserInput.Trim();
        var pending = Chat.PendingAttachments.ToList();
        if (string.IsNullOrEmpty(text) && pending.Count == 0)
        {
            return;
        }

        Chat.UserInput = string.Empty;
        Chat.ClearPendingAttachments();
        CommandManager.InvalidateRequerySuggested();

        var agentPayload = BuildChatAttachmentAgentPayload(text, pending, _projectService);
        var bubble = BuildChatAttachmentBubbleText(text, pending);
        var imagePath = pending.FirstOrDefault(a => a.IsImage)?.FilePath;
        var attachmentPaths = pending.Select(a => a.FilePath).ToList();
        var attachments = pending.Count > 0 ? pending : null;

        await ExecuteHermesUserTurnAsync(
            prependUserBubble: true,
            agentUserPayload: agentPayload,
            uiUserBubbleLine: bubble,
            uiImagePath: imagePath,
            uiAttachmentPaths: attachmentPaths.Count > 0 ? attachmentPaths : null,
            uiAttachments: attachments);
    }

    private Task SendMessageTextAsync(string text) =>
        ExecuteHermesUserTurnAsync(prependUserBubble: true, agentUserPayload: text, uiUserBubbleLine: null);

    private bool CanOpenMt5TerminalTestCommands() =>
        !_isBusy
        && Projects.SelectedProject is not null
        && Mt5TerminalTradeRouter.IsMt5TerminalProject(Projects.SelectedProject.Name);

    private bool CanOpenMt5TerminalTestOrders() =>
        !_isBusy && ResolveMt5TerminalProjectPath() is not null;

    private void OpenMt5TerminalTestCommands()
    {
        if (_mt5TestCommandsWindow != null)
        {
            try
            {
                if (_mt5TestCommandsWindow.IsLoaded)
                {
                    _mt5TestCommandsWindow.Activate();
                    return;
                }
            }
            catch
            {
                _mt5TestCommandsWindow = null;
            }
        }

        _mt5TestCommandsWindow = new Mt5TerminalTestCommandsWindow(SendMessageTextAsync)
        {
            Owner = Application.Current?.MainWindow,
        };
        _mt5TestCommandsWindow.Closed += (_, _) => _mt5TestCommandsWindow = null;
        _mt5TestCommandsWindow.Show();
    }

    private void OpenMt5TerminalTestOrders()
    {
        if (_mt5TestOrdersWindow != null)
        {
            try
            {
                if (_mt5TestOrdersWindow.IsLoaded)
                {
                    _mt5TestOrdersWindow.Activate();
                    return;
                }
            }
            catch
            {
                _mt5TestOrdersWindow = null;
            }
        }

        _mt5TestOrdersWindow = new Mt5TerminalTestOrdersWindow(
            ResolveMt5TerminalProjectPath,
            ApplyMt5TerminalTestOrderAsync)
        {
            Owner = Application.Current?.MainWindow,
        };
        _mt5TestOrdersWindow.Closed += (_, _) => _mt5TestOrdersWindow = null;
        _mt5TestOrdersWindow.Show();
    }

    private async Task<string> ApplyMt5TerminalTestOrderAsync(Mt5TerminalRouteCommand command)
    {
        var path = ResolveMt5TerminalProjectPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "FAIL: проект Mt5Terminal не найден";
        }

        AppendTerminal($"[mt5-test-order] {command.Action} id={command.Id}");
        var timeout = TimeSpan.FromSeconds(45);
        var exec = await _mt5TerminalIpc.ExecuteAsync(command, path, timeout).ConfigureAwait(true);
        var msg = Mt5TerminalIpcClient.FormatChatMessage(command, exec);

        var projectName = Projects.SelectedProject?.Name;
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "Mt5Terminal";
        }

        // Prefer showing report in Mt5Terminal chat history.
        if (!Mt5TerminalTradeRouter.IsMt5TerminalProject(projectName))
        {
            var mt5 = Projects.Projects.FirstOrDefault(p => Mt5TerminalTradeRouter.IsMt5TerminalProject(p.Name));
            if (mt5 is not null)
            {
                Projects.SelectedProject = mt5;
                projectName = mt5.Name;
                await EnsureSelectedProjectHistoryLoadedAsync().ConfigureAwait(true);
            }
        }

        var userLine = BuildTestOrderUserBubble(command);
        Chat.Messages.Add(new ChatMessage { Role = "User", Text = userLine });
        Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = msg });
        _chatLogService.AppendMessage(projectName, "User", userLine);
        _chatLogService.AppendMessage(projectName, "Hermes", msg);
        await TrySaveHistoryAfterTurnAsync(projectName).ConfigureAwait(true);
        RequestChatScrollToBottom();
        AppendTerminal(
            $"[mt5-test-order] ok={exec.Ok}" + (string.IsNullOrWhiteSpace(exec.Error) ? string.Empty : " " + exec.Error),
            isError: !exec.Ok);

        return exec.Ok ? "OK: " + (exec.Message ?? command.Action) : "FAIL: " + (exec.Error ?? "unknown");
    }

    private static string BuildTestOrderUserBubble(Mt5TerminalRouteCommand cmd)
    {
        var a = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (a is "buy_market" or "buy")
        {
            return $"[test order] Buy Market lot={cmd.Lot?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} {cmd.Symbol}";
        }

        if (a is "sell_market" or "sell")
        {
            return $"[test order] Sell Market lot={cmd.Lot?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} {cmd.Symbol}";
        }

        if (a == "place_pending")
        {
            return $"[test order] {cmd.PendingOrderType} {cmd.Symbol} @ {cmd.Price} SL={cmd.StopLoss} TP={cmd.TakeProfit} lot={cmd.Lot}";
        }

        return $"[test order] {cmd.Action}";
    }

    private async Task TickMt5AgentChatInjectAsync()
    {
        if (_mt5AgentChatInjectBusy || _isBusy)
        {
            return;
        }

        if (!Mt5TerminalAgentChatInject.TryConsume(null, out var text, out var id))
        {
            return;
        }

        _mt5AgentChatInjectBusy = true;
        try
        {
            if (!await EnsureMt5TerminalProjectSelectedAsync().ConfigureAwait(true))
            {
                AppendTerminal(
                    $"[mt5-inject] no Mt5Terminal project for id={id}: {text}",
                    isError: true);
                return;
            }

            AppendTerminal($"[mt5-inject] → agent chat id={id}: {text}");
            await SendMessageTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.LogWarn("[mt5-inject] " + ex.Message);
            AppendTerminal("[mt5-inject] " + ex.Message, isError: true);
        }
        finally
        {
            _mt5AgentChatInjectBusy = false;
        }
    }

    private async Task<bool> EnsureMt5TerminalProjectSelectedAsync()
    {
        if (Mt5TerminalTradeRouter.IsMt5TerminalProject(Projects.SelectedProject?.Name))
        {
            return true;
        }

        var mt5 = Projects.Projects.FirstOrDefault(p =>
            Mt5TerminalTradeRouter.IsMt5TerminalProject(p.Name));
        if (mt5 is null)
        {
            return false;
        }

        Projects.SelectedProject = mt5;
        await EnsureSelectedProjectHistoryLoadedAsync().ConfigureAwait(true);
        return true;
    }

    private int FindLastUserMessageIndex()
    {
        for (var i = Chat.Messages.Count - 1; i >= 0; i--)
        {
            var m = Chat.Messages[i];
            if (string.Equals(m.Role, "User", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(m.Text))
            {
                return i;
            }
        }

        return -1;
    }

    private string? FindLastUserMessageText()
    {
        var i = FindLastUserMessageIndex();
        return i >= 0 ? Chat.Messages[i].Text?.Trim() : null;
    }

    /// <summary>
    /// Edit the last user turn (dialog) and re-send to Hermes without adding another User bubble.
    /// Trailing Hermes replies after that user message are removed first.
    /// </summary>
    private async Task RetryLastUserMessageAsync()
    {
        var userIndex = FindLastUserMessageIndex();
        if (userIndex < 0)
        {
            AppendTerminal("[chat] Нет предыдущего запроса пользователя для повтора.", isError: true);
            return;
        }

        var original = Chat.Messages[userIndex].Text?.Trim() ?? string.Empty;
        var dlg = new EditRetryMessageWindow(original)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (dlg.ShowDialog() != true)
            return;

        var text = dlg.EditedText;
        if (string.IsNullOrWhiteSpace(text))
        {
            AppendTerminal("[chat] Пустое сообщение — отмена.", isError: true);
            return;
        }

        // Remove everything after the last user bubble (error or previous Hermes answer).
        while (Chat.Messages.Count > userIndex + 1)
            Chat.Messages.RemoveAt(Chat.Messages.Count - 1);

        // Update the user bubble in place when edited.
        if (!string.Equals(Chat.Messages[userIndex].Text, text, StringComparison.Ordinal))
        {
            Chat.Messages[userIndex] = new ChatMessage
            {
                Role = "User",
                Text = text,
                ImagePath = Chat.Messages[userIndex].ImagePath,
                AttachmentPaths = Chat.Messages[userIndex].AttachmentPaths,
                Attachments = Chat.Messages[userIndex].Attachments,
                Timestamp = Chat.Messages[userIndex].Timestamp,
            };
        }

        AppendTerminal(string.Equals(original, text, StringComparison.Ordinal)
            ? "[chat] Retry last user message…"
            : "[chat] Retry edited user message…");
        await ExecuteHermesUserTurnAsync(
            prependUserBubble: false,
            agentUserPayload: text,
            uiUserBubbleLine: null).ConfigureAwait(true);
    }

    /// <param name="prependUserBubble">Local chat sends <c>true</c>; inbound Supabase already pushed the bubble → <c>false</c>.</param>
    /// <param name="agentUserPayload">Plain user text forwarded to Hermes outbound prompt builder.</param>
    /// <param name="uiUserBubbleLine">When prepending user bubble and null, defaults to payload.</param>
    private async Task ExecuteHermesUserTurnAsync(
        bool prependUserBubble,
        string agentUserPayload,
        string? uiUserBubbleLine = null,
        string? uiImagePath = null,
        IReadOnlyList<string>? uiAttachmentPaths = null,
        IReadOnlyList<ChatAttachment>? uiAttachments = null)
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[chat] Select a project in the left panel (Add Project) before sending.", isError: true);
            return;
        }

        var project = Projects.SelectedProject;

        async Task PrependUserIfNeededAsync()
        {
            if (!prependUserBubble)
            {
                return;
            }

            var bubbleText = uiUserBubbleLine ?? agentUserPayload;
            Chat.Messages.Add(new ChatMessage
            {
                Role = "User",
                Text = bubbleText,
                ImagePath = uiImagePath,
                AttachmentPaths = uiAttachmentPaths,
                Attachments = uiAttachments,
            });
            _chatLogService.AppendMessage(project.Name, "User", bubbleText);
            await PublishUserTurnToSupabaseIfPossibleAsync(bubbleText);
        }

        if (TryHandleServerModeCommandLocal(agentUserPayload, project.Name))
        {
            await PrependUserIfNeededAsync();
            return;
        }

        if (Settings.HermesAgentPaused)
        {
            if (prependUserBubble)
            {
                await PrependUserIfNeededAsync();
                _logService.LogInfo(
                    "[agent] Пауза: сообщение в чат и в Supabase (если relay подключён), без вызова Hermes.");
            }
            else
            {
                _logService.LogInfo("[agent] Пауза: входящее remote-сообщение уже в чате, Hermes не вызывается.");
            }

            return;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        PushHermesThinkingStatus();
        var wslPath = ResolveHermesWslWorkingDirectory(project.WindowsPath);

        await PrependUserIfNeededAsync();

        try
        {
            if (await TryHandleReniWaterLocalAsync(agentUserPayload, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (IsPureHermesAgentChatTurn())
            {
                if (CliSessionResetTriggers.Matches(agentUserPayload))
                {
                    ResetCliSessionForProject(project.Name);
                    PostCliSessionResetReply(project.Name);
                    return;
                }

                // Mt5Terminal: «повтор» / repeat — без Hermes CLI, только WPF → RemoteTerminal.
                if (await TryHandleMt5TerminalRepeatLocalAsync(agentUserPayload, project.Name)
                        .ConfigureAwait(true))
                {
                    return;
                }

                if (await TryHandleMt5TerminalRefreshLocalAsync(agentUserPayload, project.Name)
                        .ConfigureAwait(true))
                {
                    return;
                }

                if (await TryHandleTradingAnalyticsTestSignalLocalAsync(agentUserPayload, project.Name)
                        .ConfigureAwait(true))
                {
                    return;
                }

                await ExecutePureCliAgentTurnAsync(project.Name, agentUserPayload, wslPath).ConfigureAwait(true);
                return;
            }

            if (TryHandleLocalFlashcardsViewMode(agentUserPayload, project.Name))
            {
                return;
            }

            // Trading mode: manual orders / close-position must run BEFORE desktop vision,
            // otherwise "Открой лонг по биткоину" gets routed to screen capture.
            var payloadEarly = agentUserPayload ?? string.Empty;
            if (Settings.TradingModeEnabled)
            {
                if (await TryHandleManualOrderLocalAsync(payloadEarly, project.Name).ConfigureAwait(true))
                {
                    return;
                }

                if (await TryHandleClosePositionLocalAsync(payloadEarly, project.Name).ConfigureAwait(true))
                {
                    return;
                }
            }

            if (await TryHandleDesktopScreenCaptureLocalAsync(agentUserPayload, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (await TryHandleDesktopDescribeFromCacheAsync(agentUserPayload, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (await TryHandleDesktopWindowFocusAsync(agentUserPayload, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (TryHandleBareModeSwitchLocal(payloadEarly, project.Name))
            {
                return;
            }

            if (TryHandleTradingModeGateLocal(payloadEarly, project.Name))
            {
                return;
            }

            if (await TryHandleTradingStatusQueryLocalAsync(payloadEarly, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (await TryHandleManualOrderLocalAsync(payloadEarly, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (await TryHandleClosePositionLocalAsync(payloadEarly, project.Name).ConfigureAwait(true))
            {
                return;
            }

            if (Settings.AssistantModeEnabled)
            {
                await ExecuteOpenRouterAssistantTurnAsync(project, agentUserPayload).ConfigureAwait(true);
                return;
            }

            if (_roleManager.TrySwitchRoleFromMessage(payloadEarly))
            {
                _logService.LogInfo($"[role-manager] switched from user message → {_roleManager.CurrentRole}");
            }

            string? brainBlock = null;
            if (Settings.ExternalBrainInjectIntoPrompt)
            {
                try
                {
                    var (block, items) = await _externalBrain
                        .BuildContextDetailedAsync(agentUserPayload, Settings.ExternalBrainMaxContextItems)
                        .ConfigureAwait(true);
                    brainBlock = block;
                    _lastRoleMemoryCount = items.Count;
                    _lastRoleMemoryTags = items
                        .SelectMany(static m => m.Tags)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();
                    if (!string.IsNullOrWhiteSpace(brainBlock))
                    {
                        _logService.LogInfo(
                            $"[external-brain] outbound context chars={brainBlock.Length} role={_roleManager.CurrentRole} items={items.Count}");
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogWarn($"[external-brain] BuildContext failed: {ex.Message}");
                }
            }

            var tutorWasEnabled = Settings.EnglishTutorModeEnabled;
            var payload = agentUserPayload ?? string.Empty;
            var flashcardsViewModeRequested = FlashcardsViewModePhrase.IsMatch(payload);
            var tutorDisableRequested =
                tutorWasEnabled && EnglishTutorModeTriggers.MatchesDisable(payload);
            var tutorEnableRequested =
                !flashcardsViewModeRequested
                && EnglishTutorModeTriggers.MatchesEnable(payload)
                && !tutorDisableRequested;

            EnglishTutorTurnHints tutorHints;
            if (tutorDisableRequested)
            {
                _flashcardSkill.Stop();
                EnglishTutorModeEnabled = false;
                tutorHints = new EnglishTutorTurnHints(exitedThisTurn: true, enteredThisTurn: false);
                _logService.LogInfo("[english-tutor] режим выключен (ключевая фраза в сообщении пользователя)");
                _ = PersistSettingsQuietAsync();
            }
            else if (tutorEnableRequested && !tutorWasEnabled)
            {
                _flashcardSkill.Stop();
                EnsureTradingDisabledForTutor("tutor-enable");
                EnglishTutorModeEnabled = true;
                tutorHints = new EnglishTutorTurnHints(exitedThisTurn: false, enteredThisTurn: true);
                _logService.LogInfo("[english-tutor] режим включён (ключевая фраза в сообщении пользователя)");
                _ = PersistSettingsQuietAsync();
            }
            else
            {
                tutorHints = new EnglishTutorTurnHints(
                    exitedThisTurn: false,
                    enteredThisTurn: false);
                if (tutorEnableRequested && tutorWasEnabled)
                {
                    _logService.LogInfo("[english-tutor] уже активен — повтор фразы включения проигнорирован для промпта-активации");
                }
            }

            if (Settings.EnglishTutorModeEnabled || tutorHints.ExitedThisTurn)
            {
                _logService.LogInfo(
                    $"[english-tutor] persona for prompt: enabled={Settings.EnglishTutorModeEnabled}, entered={tutorHints.EnteredThisTurn}, exited={tutorHints.ExitedThisTurn}");
            }

            var tradingWasEnabled = Settings.TradingModeEnabled;
            var tradingDisableRequested = tradingWasEnabled && TradingModeTriggers.MatchesDisable(payload);
            var tradingEnableRequested =
                TradingModeTriggers.MatchesEnable(payload) && !tradingDisableRequested;

            TradingTurnHints tradingHints;
            if (tradingDisableRequested)
            {
                ClearPendingTradingModeGate();
                _roleManager.SwitchRole(AgentRole.Universal);
                tradingHints = new TradingTurnHints(exitedThisTurn: true, enteredThisTurn: false);
                _logService.LogInfo("[trading-mode] режим агента — трейдинг выключен");
                _ = PersistSettingsQuietAsync();
            }
            else if (tradingEnableRequested && !tradingWasEnabled)
            {
                EnsureTutorDisabledForTrading("trading-enable");
                ClearPendingTradingModeGate();
                _roleManager.SwitchRole(AgentRole.Trader);
                tradingHints = new TradingTurnHints(exitedThisTurn: false, enteredThisTurn: true);
                _logService.LogInfo("[trading-mode] режим трейдинга включён");
                _ = PersistSettingsQuietAsync();
            }
            else
            {
                tradingHints = new TradingTurnHints(exitedThisTurn: false, enteredThisTurn: false);
            }

            if (Settings.TradingModeEnabled || tradingHints.ExitedThisTurn)
            {
                _logService.LogInfo(
                    $"[trading-mode] persona: enabled={Settings.TradingModeEnabled}, entered={tradingHints.EnteredThisTurn}, exited={tradingHints.ExitedThisTurn}");
            }

            var crystallizeRequested = Settings.SkillGenerationEnabled
                                       && SkillCrystallizeTriggers.Matches(payload);
            IReadOnlyList<SkillTaskMatch>? taskMatches = null;
            if (Settings.SkillGenerationEnabled && Settings.SkillAutoResolveForTasks)
            {
                taskMatches = _skillTaskMatcher.Rank(
                    payload,
                    _roleManager.CurrentRole,
                    Settings.SkillResolveMaxSuggestions,
                    Settings.SkillResolveMinScore);
            }

            SkillTurnHints skillHints;
            if (crystallizeRequested)
            {
                var reflection = SkillReflectionService.BuildFromMessages(Chat.Messages);
                skillHints = new SkillTurnHints(true, reflection, taskMatches);
                _logService.LogInfo("[skill-gen] user requested skill crystallization");
            }
            else
            {
                skillHints = new SkillTurnHints(false, null, taskMatches);
            }

            var outbound = BuildOutboundHermesPrompt(payload, brainBlock, tutorHints, tradingHints, skillHints);
            var timeout = ClampChatTimeout(Settings.ChatTimeoutSeconds);
            // HermesService uses ConfigureAwait(false) internally — force UI context before touching chat / Supabase.
            var result = await SendHermesChatWithSessionAsync(project.Name, outbound, wslPath, timeout)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                var hint = PickUserFacingHermesSummary(result);
                AppendTerminal($"[hermes] exit {result.ExitCode}: {hint}", isError: true);
                var errBubble = $"Ошибка CLI (exit {result.ExitCode}): {hint}";
                Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = errBubble });
                _chatLogService.AppendMessage(project.Name, "Hermes", errBubble);
                NotifyHermesCliFailure(project.Name, result, hint);
                await PublishAssistantTurnToSupabaseIfPossibleAsync(errBubble);
                await TrySaveHistoryAfterTurnAsync(project.Name);
                return;
            }

            var response = string.IsNullOrWhiteSpace(result.EffectiveDisplayText)
                ? "(пустой ответ)"
                : result.EffectiveDisplayText;
            await _englishTutorVocabulary.TryMergeAssistantTailAsync(response).ConfigureAwait(true);
            var displayResponse = response;
            string? assistantImagePath = null;
            MemoryDraft? turnExperienceDraft = null;

            if (Settings.SkillGenerationEnabled
                && SkillCrystallizeIntentParser.TryConsumeSaveIntent(response, out var skillSave)
                && skillSave is not null)
            {
                var saveResult = await _skillGeneration
                    .TrySaveAsync(skillSave, response, _externalBrain)
                    .ConfigureAwait(true);
                displayResponse = saveResult.UserMessage;
                ReloadGeneratedSkillsCatalog();
            }
            else if (Settings.SkillGenerationEnabled
                     && SkillCrystallizeIntentParser.TryConsumeRunIntent(response, out var runSkillId)
                     && runSkillId is not null)
            {
                if (string.Equals(runSkillId, "builtin_reni_water", StringComparison.OrdinalIgnoreCase))
                {
                    (displayResponse, assistantImagePath, turnExperienceDraft) = await ProcessWpfLocalTurnAsync(
                        response,
                        new WpfLocalIntent { Action = "reni_water_submit" },
                        project.Name,
                        payload,
                        wslPath,
                        "cli-run_generated").ConfigureAwait(true);
                    _roleManager.RecordTurn(payload, runSkillId);
                }
                else
                {
                    var skill = _generatedSkillCatalog.FindById(runSkillId);
                    if (skill is not null)
                    {
                        var run = await _generatedSkillRunner.RunAsync(skill).ConfigureAwait(true);
                        _roleSkillIndex.RecordUsage(runSkillId, _roleManager.CurrentRole);
                        _roleManager.RecordTurn(payload, runSkillId);
                        displayResponse = SkillCrystallizeIntentParser.UserFacingRunLine(runSkillId, run.Ok, run.Detail);
                    }
                    else
                    {
                        displayResponse = $"[skill] Навык «{runSkillId}» не найден в каталоге.";
                    }
                }
            }
            else if (WpfLocalIntentParser.TryConsumeIntent(response, out var wpfIntent) && wpfIntent is not null)
            {
                (displayResponse, assistantImagePath, turnExperienceDraft) = await ProcessWpfLocalTurnAsync(
                    response,
                    wpfIntent,
                    project.Name,
                    payload,
                    wslPath,
                    "cli").ConfigureAwait(true);
                _roleManager.RecordTurn(payload, wpfIntent.Action);
            }
            else if (FlashcardRelayIntentParser.TryConsumeIntent(response, out var fcKind, out var fcStart))
            {
                if (fcKind == FlashcardRelayIntentParser.FlashcardRelayIntentKind.Stop)
                {
                    _flashcardSkill.Stop();
                }
                else if (fcKind == FlashcardRelayIntentParser.FlashcardRelayIntentKind.Start && fcStart is not null)
                {
                    // If flashcards are requested, we should not be in tutor mode.
                    EnsureTutorDisabledForFlashcards("start");
                    _flashcardSkill.Start(fcStart.Topic, fcStart.IntervalMinutes, fcStart.DelayMinutes);
                }

                var nicer = FlashcardRelayIntentParser.UserFacingLine(fcKind, fcStart);
                if (!string.IsNullOrEmpty(nicer))
                {
                    // Не смешиваем сообщения «стоп карточек», если пользователь явно закрывает репетитора —
                    // модель иногда ошибочно отвечает только {"skill":"flashcard_stop"}.
                    if (tutorDisableRequested
                        && fcKind == FlashcardRelayIntentParser.FlashcardRelayIntentKind.Stop)
                    {
                        displayResponse =
                            $"{EnglishTutorPromptDefaults.DisableAckSentence} "
                            + "(Если цикл карточек был активен Hermes.Wpf — он также остановлен.)";
                        _logService.LogInfo(
                            "[flashcards] Ответ содержит flashcard_stop при явном выключении репетитора — показано сообщение про репетитора.");
                    }
                    else
                    {
                        displayResponse = nicer;
                    }
                }
            }
            else if (Settings.TradingModeEnabled
                     && TradingPlatformIntentParser.TryConsumeIntent(
                         response, out var tradingCmd, out var tradingQueryOnly, out var tradingMarket))
            {
                if (tradingQueryOnly)
                {
                    _logService.LogInfo("[trading-bridge] query intent — snapshot already in outbound prompt");
                }
                else if (tradingCmd is not null)
                {
                    var useFutures = ShouldRouteTradingToFutures(tradingMarket, tradingCmd.Action);
                    _chatLogService.AppendMessage(
                        project.Name,
                        "System",
                        $"[{(useFutures ? "futures" : "spot")}-cmd] {tradingCmd.Action} {tradingCmd.Symbol}");

                    var sentLine = TradingExecutionMessages.FormatCommandSent(tradingCmd, useFutures);
                    DispatchToUi(() => Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = sentLine }));
                    _chatLogService.AppendMessage(project.Name, "Hermes", sentLine);

                    var tradeResult = useFutures
                        ? await FuturesTradingCommandExecutor.ExecuteAsync(_futuresBridge, tradingCmd).ConfigureAwait(true)
                        : await SpotTradingCommandExecutor.ExecuteAsync(_spotBridge, tradingCmd).ConfigureAwait(true);

                    displayResponse = TradingExecutionMessages.FormatCommandResult(
                        tradeResult.Ok, tradeResult.Detail, tradingCmd, useFutures);
                    _logService.LogInfo(
                        $"[{(useFutures ? "futures" : "spot")}-bridge] {tradingCmd.Action} ok={tradeResult.Ok} detail={tradeResult.Detail}");
                    _chatLogService.AppendMessage(
                        project.Name,
                        "System",
                        $"[{(useFutures ? "futures" : "spot")}-result] ok={tradeResult.Ok} {tradeResult.Detail}");
                }
            }

            if (tradingHints.ExitedThisTurn)
            {
                displayResponse = HermesModeAcknowledgments.AgentModeActivated;
            }
            else if (tradingHints.EnteredThisTurn && TradingModeTriggers.IsBareEnableCommand(payload))
            {
                displayResponse = HermesModeAcknowledgments.TradingModeActivated;
            }

            var chatDisplay = HermesReplySplit.ForChatDisplay(displayResponse);
            if (!string.IsNullOrEmpty(assistantImagePath))
            {
                var img = ResolveExistingScreenshotPath(assistantImagePath);
                Chat.Messages.Add(
                    new ChatMessage
                    {
                        Role = "Hermes",
                        Text = chatDisplay,
                        ImagePath = img,
                    });
                var logLine = img is null ? chatDisplay : $"{chatDisplay} [image:{img}]";
                _chatLogService.AppendMessage(project.Name, "Hermes", logLine);
            }
            else
            {
                Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = chatDisplay });
                _chatLogService.AppendMessage(project.Name, "Hermes", chatDisplay);
            }

            _lastExperienceDraft = turnExperienceDraft ?? _memoryExtractor.ExtractExperience(payload, displayResponse);
            _roleManager.RecordTurn(payload);
            var vaultPath = _externalBrain.ResolveEffectiveMemoryPath();
            if (_lastExperienceDraft is not null
                && await _roleExperienceCapture
                    .TryCaptureAsync(_lastExperienceDraft, _roleManager.CurrentRole, vaultPath)
                    .ConfigureAwait(true))
            {
                _externalBrain.RestartWatcherAndReload("role-capture");
            }

            NotifyHermesReplyArrived(project.Name, chatDisplay);
            SyncWslAgentMemoryToVault("after-chat");
            SyncPlatformKnowledgeToVault("after-chat");
            _ = WslMemory.RefreshAsync();
            await PublishAssistantTurnToSupabaseIfPossibleAsync(displayResponse);
            await TrySaveHistoryAfterTurnAsync(project.Name);
        }
        catch (Exception ex)
        {
            AppendTerminal($"[error] {ex.Message}", isError: true);
            _chatLogService.AppendMessage(project.Name, "System", $"Exception: {ex.Message}");
        }
        finally
        {
            ClearHermesUiActivityTrackers();
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task<bool> TryHandleReniWaterLocalAsync(string userPayload, string projectName)
    {
        // CLI-first (Nous Research design): let Hermes CLI drive Reni Water via its skill + browser tool + project data.
        // The WPF Playwright/Task Scheduler path stays as fallback (toggle ReniWaterUseCliAgent = false).
        if (Settings.ReniWaterUseCliAgent)
        {
            return false;
        }

        ReniWaterLocalHandleResult? result;
        try
        {
            result = await _reniWaterLocalChat.TryHandleAsync(userPayload, projectName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var err = $"[reni-water] Ошибка: {ex.Message}";
            _logService.LogError(err);
            AppendTerminal(err, isError: true);
            PostLocalHermesReply(projectName, err);
            return true;
        }

        if (result is null)
        {
            return false;
        }

        if (result.ReniBusy)
        {
            _reniWaterBusy = true;
            CommandManager.InvalidateRequerySuggested();
        }

        try
        {
            AppendTerminal($"[reni-water] local handler ok={result.Ok}");
            await AppendReniWaterChatAsync(projectName, result.DisplayText, result.ScreenshotPath)
                .ConfigureAwait(true);

            if (!string.IsNullOrEmpty(result.ScreenshotPath))
            {
                OpenImageViewer(result.ScreenshotPath);
            }

            RefreshReniWaterPendingUi();
            await TrySaveHistoryAfterTurnAsync(projectName).ConfigureAwait(true);
            RequestChatScrollToBottom();
            return true;
        }
        finally
        {
            if (result.ReniBusy)
            {
                _reniWaterBusy = false;
                RefreshReniWaterPendingUi();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private async Task<(string DisplayResponse, string? ImagePath, MemoryDraft? Draft)> ProcessWpfLocalTurnAsync(
        string cliResponse,
        WpfLocalIntent intent,
        string? projectName,
        string userPayload,
        string wslPath,
        string triggerSource)
    {
        AppendTerminal($"[wpf-local] action={intent.Action} source={triggerSource}");
        if (intent.Action.StartsWith("reni_water", StringComparison.Ordinal))
        {
            _reniWaterBusy = true;
            CommandManager.InvalidateRequerySuggested();
        }

        try
        {
            var exec = await _wpfLocalExecutor
                .ExecuteAsync(intent, projectName, userPayload, triggerSource)
                .ConfigureAwait(true);
            var display = WpfLocalIntentParser.FormatDisplayResponse(cliResponse, exec);

            if (!string.IsNullOrEmpty(projectName))
            {
                _chatLogService.AppendMessage(
                    projectName,
                    "System",
                    $"[wpf-local] {intent.Action} ok={exec.Ok}");
            }

            if (exec.Ok
                && string.Equals(intent.Action, "reni_water_submit", StringComparison.Ordinal)
                && Settings.ReniWaterLearningSuccessCount >= Settings.ReniWaterAutoCrystallizeAfterSuccesses)
            {
                ReloadGeneratedSkillsCatalog();
            }

            if (!string.IsNullOrEmpty(exec.ScreenshotPath))
            {
                OpenImageViewer(exec.ScreenshotPath);
            }

            RefreshReniWaterPendingUi();

            // Scheduler / portfolio harness CRUD — skip CLI post-local follow-up.
            if (IsWpfLocalHarnessAction(intent.Action))
            {
                return (display, exec.ScreenshotPath, null);
            }

            var followUp = await _cliFollowUp.SendAsync(userPayload, exec, wslPath).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(followUp))
            {
                if (Settings.SkillGenerationEnabled
                    && SkillCrystallizeIntentParser.TryConsumeSaveIntent(followUp, out var fuSave)
                    && fuSave is not null)
                {
                    var saveResult = await _skillGeneration
                        .TrySaveAsync(fuSave, followUp, _externalBrain)
                        .ConfigureAwait(true);
                    followUp = saveResult.UserMessage;
                    ReloadGeneratedSkillsCatalog();
                }

                display = $"{display.Trim()}\n\n{followUp.Trim()}";
            }

            var draft = exec.LearningRecord is not null
                ? _memoryExtractor.ExtractFromLocalExecution(exec.LearningRecord)
                : _memoryExtractor.ExtractExperience(userPayload, display);

            return (display, exec.ScreenshotPath, draft);
        }
        finally
        {
            if (intent.Action.StartsWith("reni_water", StringComparison.Ordinal))
            {
                _reniWaterBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private bool TryHandleLocalFlashcardsViewMode(string userPayload, string projectName)
    {
        var text = (userPayload ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!FlashcardsViewModePhrase.IsMatch(text))
        {
            return false;
        }

        // Default: interval 3 minutes, topic "ИИ", start immediately.
        const int defaultIntervalMin = 3;
        const int defaultDelayMin = 0;
        const string defaultTopic = "ИИ";

        EnsureTutorDisabledForFlashcards("local-view-mode");
        _flashcardSkill.Start(defaultTopic, defaultIntervalMin, defaultDelayMin);

        var line = $"[flashcards] Запланировано: «{defaultTopic}» • интервал {defaultIntervalMin} мин • старт через {defaultDelayMin} мин.";
        Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = line });
        _chatLogService.AppendMessage(projectName, "Hermes", line);
        _ = PublishAssistantTurnToSupabaseIfPossibleAsync(line);
        _logService.LogInfo("[flashcards] local view-mode start (defaults applied).");
        return true;
    }

    private async Task<bool> TryHandleDesktopScreenCaptureLocalAsync(string userPayload, string projectName)
    {
        if (!DesktopScreenCaptureTriggers.Matches(userPayload))
        {
            return false;
        }

        await RunDesktopScreenCaptureUiAsync(
                projectName,
                userPayload,
                showVisionStatus: true,
                focusWindowTarget: null)
            .ConfigureAwait(true);
        return true;
    }

    private async Task<bool> TryHandleDesktopWindowFocusAsync(string userPayload, string projectName)
    {
        if (TradingManualOrderParser.LooksLikeTradeIntent(userPayload))
        {
            return false;
        }

        if (!DesktopWindowFocusTriggers.TryParseTarget(userPayload, out var target))
        {
            return false;
        }

        await RunDesktopScreenCaptureUiAsync(
                projectName,
                userPayload,
                showVisionStatus: true,
                focusWindowTarget: target)
            .ConfigureAwait(true);
        return true;
    }

    private async Task<bool> TryHandleDesktopDescribeFromCacheAsync(string userPayload, string projectName)
    {
        if (!DesktopVisionIntentDetector.WantsDetailedReport(userPayload)
            || DesktopScreenCaptureTriggers.Matches(userPayload))
        {
            return false;
        }

        var snap = _desktopScreenContext.GetFresh();
        if (snap is null || !_desktopVisionSkill.IsEnabled || Settings.HermesAgentPaused)
        {
            return false;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        AgentChatStatusLine = "Hermes готовит описание экрана…";
        IsAgentChatStatusBarVisible = true;

        try
        {
            var projectPath = Projects.SelectedProject?.WindowsPath ?? Settings.WorkspaceRootWindowsPath;
            var wslPath = ResolveHermesWslWorkingDirectory(projectPath);
            var annotatedWsl = _projectService.ConvertToWslPath(snap.AnnotatedImagePath);
            var metaWsl = _projectService.ConvertToWslPath(snap.MetadataPath);
            var visionRequest = DesktopVisionPromptBuilder.BuildDescribeFromCacheRequest(snap, annotatedWsl, metaWsl);
            var outbound = BuildOutboundDesktopVisionPrompt(visionRequest);
            var analysis = await _desktopVisionSkill
                .RunVisionPromptAsync(wslPath, outbound)
                .ConfigureAwait(true);

            var chatText = analysis.Success
                ? analysis.UserVisible ?? analysis.RawText ?? "Описание недоступно."
                : $"Не удалось описать экран: {analysis.Error}";

            if (analysis.Success && !string.IsNullOrWhiteSpace(analysis.InternalContext))
            {
                _desktopScreenContext.RefreshInternalContext(
                    analysis.InternalContext,
                    DesktopVisionIntent.DescribeScreen,
                    snap.FocusWindowTitle);
            }

            Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = chatText, ImagePath = snap.AnnotatedImagePath });
            _chatLogService.AppendMessage(
                projectName,
                "Hermes",
                $"{chatText} [image:{snap.AnnotatedImagePath}]");
            await PublishAssistantTurnToSupabaseIfPossibleAsync(chatText).ConfigureAwait(true);
            await SaveHistoryAsync(projectName).ConfigureAwait(true);
        }
        finally
        {
            ClearHermesUiActivityTrackers();
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }

        return true;
    }

    private async Task RunDesktopScreenCaptureWithBusyAsync()
    {
        if (Projects.SelectedProject is null)
        {
            AppendTerminal("[screen-capture] Выберите проект в левой панели (Add Project).", isError: true);
            return;
        }

        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await RunDesktopScreenCaptureUiAsync(
                    Projects.SelectedProject.Name,
                    userRequest: null,
                    showVisionStatus: true)
                .ConfigureAwait(true);
        }
        finally
        {
            ClearHermesUiActivityTrackers();
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunDesktopScreenCaptureUiAsync(
        string? projectName = null,
        string? userRequest = null,
        bool showVisionStatus = false,
        string? focusWindowTarget = null)
    {
        try
        {
            if (showVisionStatus)
            {
                AgentChatStatusLine = "Снимок экрана…";
                IsAgentChatStatusBarVisible = true;
            }

            var capture = await Task.Run(() => _desktopScreenCapture.CapturePrimaryMonitor()).ConfigureAwait(true);
            var regionCount = capture.Regions.Count;
            var runVision = !string.IsNullOrWhiteSpace(projectName)
                            && _desktopVisionSkill.IsEnabled
                            && !Settings.HermesAgentPaused;

            var focusTarget = focusWindowTarget;
            if (string.IsNullOrWhiteSpace(focusTarget)
                && DesktopWindowFocusTriggers.TryParseTarget(userRequest, out var parsedFocus))
            {
                focusTarget = parsedFocus;
            }

            var intent = DesktopVisionIntentDetector.Resolve(userRequest, focusTarget);
            var chatText = ScreenCaptureSummaryBuilder.BuildChatSummary(capture, runVision);

            AppendTerminal($"[screen-capture] {capture.AnnotatedImagePath}");
            if (!string.IsNullOrWhiteSpace(capture.DuplicateDirectory))
            {
                AppendTerminal($"[screen-capture] копия → {capture.DuplicateDirectory}");
            }

            _logService.LogInfo(
                $"[screen-capture] regions={regionCount}, intent={intent}, meta={capture.MetadataPath}, vision={runVision}");

            await _hermesGalleryPublisher.TryPublishPlainScreenshotAsync(capture).ConfigureAwait(true);

            if (runVision)
            {
                if (showVisionStatus)
                {
                    AgentChatStatusLine = intent == DesktopVisionIntent.FocusWindow
                        ? "Hermes размечает окно…"
                        : "Hermes анализирует скриншот (vision_analyze)…";
                    IsAgentChatStatusBarVisible = true;
                }

                var projectPath = Projects.SelectedProject?.WindowsPath
                                  ?? Settings.WorkspaceRootWindowsPath;
                var wslPath = ResolveHermesWslWorkingDirectory(projectPath);
                var annotatedWsl = _projectService.ConvertToWslPath(capture.AnnotatedImagePath);
                var plainWsl = _projectService.ConvertToWslPath(capture.ImagePath);
                var metaWsl = _projectService.ConvertToWslPath(capture.MetadataPath);
                var visionRequest = DesktopVisionPromptBuilder.BuildUserRequest(
                    capture,
                    annotatedWsl,
                    plainWsl,
                    metaWsl,
                    intent,
                    userRequest,
                    focusTarget);
                var outbound = BuildOutboundDesktopVisionPrompt(visionRequest);
                var analysis = await _desktopVisionSkill
                    .AnalyzeCaptureAsync(capture, wslPath, outbound)
                    .ConfigureAwait(true);

                if (analysis.Success)
                {
                    var internalCtx = analysis.InternalContext ?? analysis.RawText ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(internalCtx))
                    {
                        _desktopScreenContext.Save(capture, internalCtx, intent, focusTarget);
                        _logService.LogInfo($"[desktop-vision] context saved, chars={internalCtx.Length}");
                    }

                    chatText = ComposeVisionChatText(capture, intent, analysis);
                }
                else if (!analysis.Skipped && !string.IsNullOrWhiteSpace(analysis.Error))
                {
                    chatText = $"{DesktopCaptureUserMessages.BriefCaptureOnly(capture)} Ошибка анализа: {analysis.Error}";
                }
            }

            if (!string.IsNullOrWhiteSpace(projectName))
            {
                await AppendDesktopCaptureChatAsync(projectName, chatText, capture.AnnotatedImagePath)
                    .ConfigureAwait(true);
                await SaveHistoryAsync(projectName).ConfigureAwait(true);
            }

            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current?.MainWindow;
            if (!ScreenCaptureViewerService.TryShow(capture, showAnnotated: true, owner))
            {
                AppendTerminal("[screen-capture] Не удалось открыть окно просмотра.", isError: true);
            }
        }
        catch (Exception ex)
        {
            AppendTerminal($"[screen-capture] {ex.Message}", isError: true);
            _logService.LogError($"[screen-capture] {ex}");
        }
    }

    private static string ComposeVisionChatText(
        ScreenCaptureResult capture,
        DesktopVisionIntent intent,
        DesktopVisionAnalysisResult analysis)
    {
        if (intent == DesktopVisionIntent.DescribeScreen)
        {
            return !string.IsNullOrWhiteSpace(analysis.UserVisible)
                ? analysis.UserVisible!
                : analysis.RawText ?? DesktopCaptureUserMessages.BriefCaptureOnly(capture);
        }

        return DesktopCaptureUserMessages.BriefAfterCapture(capture, analysis.UserVisible);
    }

    private Task AppendDesktopCaptureChatAsync(string projectName, string text, string imagePath) =>
        AppendAssistantChatWithImageAsync(projectName, text, imagePath);

    private void RefreshReniWaterPendingUi()
    {
        var pending = _reniWater.ReadPendingAck();
        if (pending is null)
        {
            IsReniWaterStatusBarVisible = false;
            ReniWaterStatusText = string.Empty;
            _reniWaterPendingScreenshotPath = null;
            CanViewReniWaterScreenshot = false;
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        IsReniWaterStatusBarVisible = true;
        var auth = pending.AuthRequired ? " Требуется вход на сайт." : string.Empty;
        ReniWaterStatusText =
            $"{ReniWaterUserMessages.SubmitSuccess} {ReniWaterUserMessages.SubmitPendingAckReminder}{auth} "
            + "Нажмите «Подтвердить» или напишите «принял».";

        _reniWaterPendingScreenshotPath = ResolveExistingScreenshotPath(pending.ScreenshotPath);
        CanViewReniWaterScreenshot = !string.IsNullOrEmpty(_reniWaterPendingScreenshotPath);
        CommandManager.InvalidateRequerySuggested();
    }

    public void OpenImageViewer(string? imagePath)
    {
        var path = ResolveExistingScreenshotPath(imagePath);
        if (string.IsNullOrEmpty(path))
        {
            AppendTerminal("[image] Файл скриншота не найден.", isError: true);
            return;
        }

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        if (!ImageViewerService.TryShow(path, owner))
        {
            AppendTerminal("[image] Не удалось открыть скриншот.", isError: true);
        }
    }

    private void ShowReniWaterScreenshotInChat()
    {
        var path = _reniWaterPendingScreenshotPath;
        if (string.IsNullOrEmpty(path))
        {
            path = _reniWater.ReadPendingAck()?.ScreenshotPath;
        }

        OpenImageViewer(path);
    }

    private Task AppendReniWaterChatAsync(string projectName, string text, string? screenshotPath) =>
        AppendAssistantChatWithImageAsync(projectName, text, screenshotPath);

    private async Task AppendAssistantChatWithImageAsync(string projectName, string text, string? imagePath)
    {
        imagePath = ResolveExistingScreenshotPath(imagePath);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Chat.Messages.Add(
                new ChatMessage
                {
                    Role = "Hermes",
                    Text = text,
                    ImagePath = imagePath,
                });
        });

        var logLine = imagePath is null ? text : $"{text} [image:{imagePath}]";
        _chatLogService.AppendMessage(projectName, "Hermes", logLine);
        await PublishAssistantTurnToSupabaseIfPossibleAsync(logLine).ConfigureAwait(true);
    }

    private static string? ResolveExistingScreenshotPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path.Trim());
        return File.Exists(full) ? full : null;
    }

    private void EnsureTutorDisabledForFlashcards(string reasonTag)
    {
        if (!Settings.EnglishTutorModeEnabled)
        {
            return;
        }

        SetEnglishTutorModeEnabledQuiet(false);
        _logService.LogInfo($"[english-tutor] авто-выход из режима (flashcards: {reasonTag})");
        _ = PersistSettingsQuietAsync();
    }

    private void EnsureTutorDisabledForTrading(string reasonTag)
    {
        if (!Settings.EnglishTutorModeEnabled)
        {
            return;
        }

        SetEnglishTutorModeEnabledQuiet(false);
        _logService.LogInfo($"[english-tutor] авто-выход (trading-mode: {reasonTag})");
        _ = PersistSettingsQuietAsync();
    }

    /// <summary>Updates tutor flag without Supabase session publish (caller publishes final mode).</summary>
    private void SetEnglishTutorModeEnabledQuiet(bool value)
    {
        if (Settings.EnglishTutorModeEnabled == value)
        {
            return;
        }

        Settings.EnglishTutorModeEnabled = value;
        RaisePropertyChanged(nameof(EnglishTutorModeEnabled));
        RaisePropertyChanged(nameof(EnglishTutorStatusRibbonText));
        RaisePropertyChanged(nameof(IsEnglishTutorStatusVisible));
        RefreshChatModeStatusUi();
    }

    private void EnsureTradingDisabledForTutor(string reasonTag)
    {
        if (!Settings.TradingModeEnabled)
        {
            return;
        }

        ClearPendingTradingModeGate();
        if (_roleManager.CurrentRole == AgentRole.Trader)
        {
            _roleManager.SwitchRole(AgentRole.Universal);
        }

        _logService.LogInfo($"[trading-mode] авто-выход (english-tutor: {reasonTag})");
        _ = PersistSettingsQuietAsync();
    }

    private async Task<bool> TryHandleTradingStatusQueryLocalAsync(string payload, string projectName)
    {
        if (!Settings.TradingModeEnabled)
        {
            return false;
        }

        var intent = TradingQueryIntentClassifier.Classify(payload);
        if (intent == TradingQueryIntent.None)
        {
            return false;
        }

        if (!await _futuresBridge.EnsureTerminalReadyAsync(force: true).ConfigureAwait(true))
        {
            PostLocalHermesReply(projectName, FuturesTerminalStatusReplyFormatter.TerminalUnavailableMessage());
            _logService.LogInfo($"[trading-status] {intent}: Futures terminal not running");
            return true;
        }

        var futures = _futuresBridge.TryReadFuturesSection();
        if (futures is null)
        {
            PostLocalHermesReply(projectName, FuturesTerminalStatusReplyFormatter.TerminalUnavailableMessage());
            _logService.LogInfo($"[trading-status] {intent}: futures snapshot missing");
            return true;
        }

        var reply = intent switch
        {
            TradingQueryIntent.BalanceOnly => FuturesTerminalStatusReplyFormatter.FormatBalanceOnly(futures),
            TradingQueryIntent.AccountSummary => FuturesTerminalStatusReplyFormatter.FormatAccountSummary(futures),
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(reply))
        {
            return false;
        }

        PostLocalHermesReply(projectName, reply);
        _logService.LogInfo($"[trading-status] local reply ({intent}) from Futures terminal");
        return true;
    }

    private void ClearPendingTradingModeGate()
    {
        _pendingTradingModeSwitch = false;
        _pendingTradingModeOriginalPayload = null;
    }

    private string? TakePendingTradingModeOriginalPayload()
    {
        var original = _pendingTradingModeOriginalPayload;
        _pendingTradingModeOriginalPayload = null;
        return original;
    }

    private async Task ContinueDeferredTurnAfterTradingGateAsync(string originalPayload, bool skipTradingGate)
    {
        if (string.IsNullOrWhiteSpace(originalPayload))
        {
            return;
        }

        if (skipTradingGate)
        {
            _skipTradingModeGateOnce = true;
        }

        _logService.LogInfo($"[trading-mode] продолжение отложенной команды (chars={originalPayload.Length})");
        await ExecuteHermesUserTurnAsync(prependUserBubble: false, agentUserPayload: originalPayload).ConfigureAwait(true);
    }

    private bool TryHandleTradingModeGateLocal(string payload, string projectName)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        if (_skipTradingModeGateOnce)
        {
            _skipTradingModeGateOnce = false;
            return false;
        }

        if (_pendingTradingModeSwitch)
        {
            if (TradingModeTriggers.MatchesConfirmNo(payload))
            {
                var deferred = TakePendingTradingModeOriginalPayload();
                ClearPendingTradingModeGate();
                PostTradingModeGateReply(
                    projectName,
                    "Остаёмся в режиме агента. Торговые команды — после «трейдинг» или «trading».");
                if (!string.IsNullOrWhiteSpace(deferred))
                {
                    _ = ContinueDeferredTurnAfterTradingGateAsync(deferred, skipTradingGate: true);
                }

                return true;
            }

            if (TradingModeTriggers.MatchesConfirmYes(payload))
            {
                EnsureTutorDisabledForTrading("confirm-yes");
                var deferred = TakePendingTradingModeOriginalPayload();
                ClearPendingTradingModeGate();
                _roleManager.SwitchRole(AgentRole.Trader);
                _ = PersistSettingsQuietAsync();
                _logService.LogInfo("[trading-mode] подтверждён переход в режим трейдинга");

                EnsureSpotTerminalRunning(force: true);
                RefreshChatModeStatusUi();

                if (IsBareTradingConfirmMessage(payload))
                {
                    PostTradingModeGateReply(projectName, HermesModeAcknowledgments.TradingModeActivated);
                }

                if (!string.IsNullOrWhiteSpace(deferred))
                {
                    _ = ContinueDeferredTurnAfterTradingGateAsync(deferred, skipTradingGate: false);
                }

                return true;
            }
        }

        if (!Settings.TradingModeEnabled
            && !_pendingTradingModeSwitch
            && TradingTaskDetector.IsTradingRelated(payload))
        {
            _pendingTradingModeSwitch = true;
            _pendingTradingModeOriginalPayload = payload.Trim();
            PostTradingModeGateReply(projectName, TradingModePromptDefaults.SwitchPromptUserBubble);
            _logService.LogInfo("[trading-mode] запрос подтверждения переключения");
            return true;
        }

        return false;
    }

    private static bool IsBareTradingConfirmMessage(string payload)
    {
        var t = payload.Trim().ToLowerInvariant();
        return t is "да" or "yes" or "ok" or "ок" or "okay" or "ага" or "угу" or "конечно" or "давай";
    }

    private void PostTradingModeGateReply(string projectName, string text) =>
        PostLocalHermesReply(projectName, text);

    private bool TryHandleServerModeCommandLocal(string payload, string projectName)
    {
        if (!ServerModeCommandParser.TryParse(payload, out var mode))
        {
            return false;
        }

        if (ServerModeCommandParser.IsAssistantMode(mode))
        {
            _logService.LogInfo($"[assistant-mode] server mode command parsed from: {payload.Trim()}");
            ApplyAssistantModeSwitch(projectName, $"[assistant-mode] server JSON mode={mode}");
            return true;
        }

        return false;
    }

    private bool ApplyAssistantModeSwitch(string projectName, string logLine)
    {
        ClearPendingTradingModeGate();
        var roleChanged = _roleManager.CurrentRole != AgentRole.Universal;
        if (roleChanged)
        {
            _suppressRoleChangeModeNotice = true;
        }

        AssistantModeEnabled = true;
        if (roleChanged)
        {
            _roleManager.SwitchRole(AgentRole.Universal);
        }

        _ = PersistSettingsQuietAsync();
        PostModeStatusNotice(projectName);
        _logService.LogInfo(logLine);
        return true;
    }

    private bool TryHandleBareModeSwitchLocal(string payload, string projectName)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        if (AssistantModeTriggers.IsBareEnableCommand(payload))
        {
            ApplyAssistantModeSwitch(projectName, "[assistant-mode] bare switch → assistant mode (OpenRouter)");
            return true;
        }

        if (TradingModeTriggers.IsBareAgentModeCommand(payload))
        {
            ClearPendingTradingModeGate();
            AssistantModeEnabled = false;
            if (_roleManager.CurrentRole == AgentRole.Trader)
            {
                _roleManager.SwitchRole(AgentRole.Universal);
                _ = PersistSettingsQuietAsync();
            }

            PostModeStatusNotice(projectName);
            _logService.LogInfo("[trading-mode] bare switch → agent mode (local ack)");
            return true;
        }

        if (TradingModeTriggers.IsBareEnableCommand(payload))
        {
            var deferred = TakePendingTradingModeOriginalPayload();
            ClearPendingTradingModeGate();
            AssistantModeEnabled = false;
            EnsureTutorDisabledForTrading("bare-enable");
            if (_roleManager.CurrentRole != AgentRole.Trader)
            {
                _roleManager.SwitchRole(AgentRole.Trader);
                _ = PersistSettingsQuietAsync();
            }

            EnsureSpotTerminalRunning(force: true);
            PostModeStatusNotice(projectName);
            RefreshChatModeStatusUi();
            _logService.LogInfo("[trading-mode] bare switch → trading mode (local ack)");
            if (!string.IsNullOrWhiteSpace(deferred))
            {
                _ = ContinueDeferredTurnAfterTradingGateAsync(deferred, skipTradingGate: false);
            }

            return true;
        }

        return false;
    }

    private void PostModeStatusNotice(string projectName, bool persistHistory = true)
    {
        RefreshChatModeStatusUi();
        SyncChatModeStatusBubble(persistHistory);
        _ = PublishSessionContextToSupabaseIfChangedAsync("mode-notice");
    }

    private void SyncChatModeStatusBubble(bool persistHistory)
    {
        RefreshChatModeStatusUi();
        var line = ChatModeStatusText;
        for (var i = Chat.Messages.Count - 1; i >= 0; i--)
        {
            if (Chat.Messages[i].Role == "Hermes"
                && HermesChatModeResolver.IsChatModeStatusLine(Chat.Messages[i].Text))
            {
                if (string.Equals(Chat.Messages[i].Text, line, StringComparison.Ordinal))
                {
                    return;
                }

                Chat.Messages.RemoveAt(i);
            }
        }

        Chat.Messages.Insert(0, new ChatMessage { Role = "Hermes", Text = line });
        RequestChatScrollToBottom();

        if (persistHistory && Projects.SelectedProject is { Name: var projectName })
        {
            _ = TrySaveHistoryAfterTurnAsync(projectName);
        }
    }

    private async Task PublishAppStartupNotificationAsync(string reason)
    {
        if (_startupSupabaseNotificationSent || !Settings.SupabaseRelayEnabled)
        {
            return;
        }

        await _sessionPublishLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_startupSupabaseNotificationSent)
            {
                return;
            }

            await EnsureSupabaseRelayReadyForPublishAsync().ConfigureAwait(true);
            if (_supabaseRelay is not { IsConnected: true })
            {
                return;
            }

            var version = AppVersion.Number;
            var json = AppLifecycleSupabasePayload.BuildStartupJson("hermes_wpf", version);
            var voice = AppLifecycleSupabasePayload.BuildSupabaseContent("Hermes Command Center");
            var label = CanonicalHermesSenderName();

            var recipient = CanonicalHermesOutboundRecipient();
            await _supabaseRelay.InsertAssistantRowAsync(label, recipient, json, CancellationToken.None, logPublish: false)
                .ConfigureAwait(true);
            _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, json);
            await _supabaseRelay.InsertAssistantRowAsync(label, recipient, voice, CancellationToken.None, logPublish: false)
                .ConfigureAwait(true);
            _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, voice);
            _startupSupabaseNotificationSent = true;
            _logService.LogInfo($"[supabase] app startup notification ({reason}): hermes_wpf v{version}");
        }
        catch (Exception ex)
        {
            _logService.LogWarn($"[supabase] app startup notification failed: {ex.Message}");
        }
        finally
        {
            _sessionPublishLock.Release();
        }
    }

    private async Task ExecuteOpenRouterAssistantTurnAsync(HermesProject project, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(Settings.InAppAssistantOpenRouterApiKey))
        {
            PostLocalHermesReply(project.Name, "Укажите OpenRouter API key в Settings → ИИ-помощник.");
            return;
        }

        var options = new AppAssistantOptions
        {
            ApplicationId = AppAssistantKnowledge.HermesWpfId,
            OpenRouterApiKey = Settings.InAppAssistantOpenRouterApiKey,
            Model = Settings.InAppAssistantOpenRouterModel,
        };

        var history = Chat.Messages
            .Where(m => m.Role is "User" or "Hermes")
            .TakeLast(24)
            .Select(m => new AssistantChatTurn(
                m.Role.Equals("User", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                m.Text))
            .ToList();

        try
        {
            var reply = await _appAssistantService
                .AskAsync(options, _assistantContextProvider, history, userMessage)
                .ConfigureAwait(true);
            PostLocalHermesReply(project.Name, reply);
        }
        catch (Exception ex)
        {
            PostLocalHermesReply(project.Name, $"OpenRouter: {ex.Message}");
            _logService.LogError($"[openrouter-assistant] main chat: {ex.Message}");
        }
    }

    private async Task<bool> TryHandleManualOrderLocalAsync(string payload, string projectName) =>
        await _tradingManualOrder.TryHandleAsync(
            payload,
            projectName,
            Settings.TradingModeEnabled,
            PostLocalHermesReplyAsync,
            AppendTradingSystemLogAsync).ConfigureAwait(true);

    private Task PostLocalHermesReplyAsync(string projectName, string text)
    {
        PostLocalHermesReply(projectName, text);
        return Task.CompletedTask;
    }

    private Task AppendTradingSystemLogAsync(string projectName, string text)
    {
        _chatLogService.AppendMessage(projectName, "System", text);
        return Task.CompletedTask;
    }

    private async Task<bool> TryHandleClosePositionLocalAsync(string payload, string projectName)
    {
        if (!Settings.TradingModeEnabled)
        {
            return false;
        }

        if (!TradingClosePositionTriggers.Matches(payload))
        {
            return false;
        }

        var useFutures = _futuresBridge.IsActiveForSession;
        var bridgeReady = useFutures
            ? await _futuresBridge.EnsureTerminalReadyAsync(force: true).ConfigureAwait(true)
            : await _spotBridge.EnsureTerminalReadyAsync(force: true).ConfigureAwait(true);

        if (!bridgeReady)
        {
            PostLocalHermesReply(
                projectName,
                useFutures
                    ? FuturesTerminalStatusReplyFormatter.TerminalUnavailableMessage()
                    : SpotTerminalStatusReplyFormatter.TerminalUnavailableMessage());
            return true;
        }

        var knownSymbols = useFutures
            ? _futuresBridge.TryReadFuturesSection()?.Positions.Select(p => p.Symbol).ToList()
            : _spotBridge.TryReadSpotSection()?.Tickers.Select(t => t.Symbol).ToList();
        var symbol = TradingSymbolResolver.ResolveFromText(payload, knownSymbols);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            PostLocalHermesReply(projectName, "Укажите инструмент (например: «закрой позицию по биткоину»).");
            return true;
        }

        var cmd = new TradingPlatformCommand
        {
            Action = "close_position",
            Symbol = symbol,
            OrderType = TradingManualOrderParser.TryParsePriceOnly(payload, out var price) && price.Kind == ManualPriceKind.Limit
                ? "Limit"
                : "Market",
            Price = price.Price,
            RequestedBy = "Hermes.Wpf-local",
        };

        PostLocalHermesReply(projectName, TradingExecutionMessages.FormatCommandSent(cmd, useFutures));
        var result = useFutures
            ? await FuturesTradingCommandExecutor.ExecuteAsync(_futuresBridge, cmd).ConfigureAwait(true)
            : await SpotTradingCommandExecutor.ExecuteAsync(_spotBridge, cmd).ConfigureAwait(true);
        PostLocalHermesReply(
            projectName,
            TradingExecutionMessages.FormatCommandResult(result.Ok, result.Detail, cmd, useFutures));
        return true;
    }

    private void PostLocalHermesReply(string projectName, string text, bool publishToSupabase = true)
    {
        DispatchToUi(() => Chat.Messages.Add(new ChatMessage { Role = "Hermes", Text = text }));
        _chatLogService.AppendMessage(projectName, "Hermes", text);
        RequestChatScrollToBottom();
        if (publishToSupabase)
        {
            _ = PublishAssistantTurnToSupabaseIfPossibleAsync(text);
        }

        _ = TrySaveHistoryAfterTurnAsync(projectName);
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
        var speakSource = HermesReplySplit.ForSpeakSource(assistantPlainText);
        var contentForSupabase = BilingualSegmentFormatter.ToSupabaseContent(speakSource);

        try
        {
            var recipient = CanonicalHermesOutboundRecipient();
            await _supabaseRelay.InsertAssistantRowAsync(label, recipient, contentForSupabase);
            _supabaseEchoTracker.RegisterAfterSuccessfulPublish(label, contentForSupabase);
            var formatKind = BilingualSegmentFormatter.ShouldPublishAsRawJson(speakSource) ? "raw" : "bilingual";
            _logService.LogInfo(
                $"[supabase] Ответ агента записан в messages (sender_name={label}, recipient_name={recipient}, chars={contentForSupabase.Length}, format={formatKind}).");
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
            if (Settings.SupabaseUseAnonymousAuth)
            {
                try
                {
                    await _supabaseRelay.EnsureFreshSessionAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logService.LogWarn($"[supabase] Не удалось обновить JWT перед публикацией: {ex.Message}");
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SupabaseUrl) ||
            string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey))
        {
            _logService.LogWarn(
                "[supabase] Не удалось восстановить клиент перед публикацией: задайте Supabase URL и anon key в настройках.");
            return;
        }

        _logService.LogWarn("[supabase] Клиент не активен перед публикацией — выполняю StartSupabaseRelayCoreAsync().");
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
            var recipient = LocalOutboundRecipient();
            await _supabaseRelay.InsertAssistantRowAsync(tag, recipient, userBubbleText, CancellationToken.None, logPublish: false);
            _logService.LogInfo(
                $"[supabase] Опубликовано пользовательское сообщение в таблицу messages (sender_name={tag}, recipient_name={recipient}).");
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

    private string CanonicalHermesOutboundRecipient()
    {
        if (!string.IsNullOrWhiteSpace(_supabaseDynamicOutboundRecipient))
        {
            return _supabaseDynamicOutboundRecipient!.Trim();
        }

        var s = Settings.SupabaseHermesOutboundRecipientName?.Trim();
        return string.IsNullOrEmpty(s) ? "Android" : s;
    }

    private string LocalOutboundRecipient()
    {
        var s = Settings.SupabaseLocalOutboundRecipientName?.Trim();
        return string.IsNullOrEmpty(s) ? "Hermes" : s;
    }

    private bool MatchesInboundRecipientFilter(SupabaseMessageRow m)
    {
        if (!Settings.SupabaseFilterInboundByRecipient)
        {
            return true;
        }

        var expected = Settings.SupabaseInboundRecipientName?.Trim();
        if (string.IsNullOrEmpty(expected))
        {
            expected = "Hermes";
        }

        return HermesProjectRecipientRouter.IsHermesBoundRecipient(m.RecipientName, expected);
    }

    /// <summary>
    /// If <c>recipient_name</c> is <c>Hermes.&lt;Project&gt;</c>, select that Project Manager project
    /// (add from disk if needed) and load its chat history before handling the message.
    /// </summary>
    private async Task<bool> EnsureProjectContextForInboundRecipientAsync(string? recipientName)
    {
        if (!HermesProjectRecipientRouter.TryParseProjectSuffix(recipientName, out var projectName))
        {
            // Plain "Hermes" — keep current selection.
            return Projects.SelectedProject is not null;
        }

        var project = HermesProjectRecipientRouter.FindInList(Projects.Projects, projectName);
        if (project is null)
        {
            var discovered = HermesProjectRecipientRouter.DiscoverProjectDirectory(
                Projects.Projects,
                projectName,
                Settings.LastProjectBrowsePath ?? Settings.LastSelectedProjectPath);
            if (discovered is null)
            {
                _logService.LogWarn(
                    $"[supabase] Нет проекта «{projectName}» для recipient_name={recipientName}. "
                    + "Добавьте папку в Project Manager.");
                AppendTerminal(
                    $"[supabase] Проект «{projectName}» не найден (recipient={recipientName}).",
                    isError: true);
                return false;
            }

            try
            {
                project = _projectService.BuildProject(discovered);
                if (!Projects.Projects.Any(p =>
                        string.Equals(p.WindowsPath, project.WindowsPath, StringComparison.OrdinalIgnoreCase)))
                {
                    Projects.Projects.Add(project);
                    SnapshotProjectsIntoSettings();
                    try { await _settingsService.SaveAsync(Settings).ConfigureAwait(true); }
                    catch (Exception ex) { _logService.LogWarn("[settings] save after project discover: " + ex.Message); }
                }

                _logService.LogInfo($"[supabase] Проект «{project.Name}» добавлен из {discovered}");
            }
            catch (Exception ex)
            {
                _logService.LogError($"[supabase] Не удалось открыть «{discovered}»: {ex.Message}");
                return false;
            }
        }

        if (Projects.SelectedProject is not null
            && string.Equals(
                Projects.SelectedProject.WindowsPath,
                project.WindowsPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _logService.LogInfo(
            $"[supabase] Переключение чата → «{project.Name}» (recipient_name={recipientName})");
        AppendTerminal($"[supabase] Открыт чат проекта «{project.Name}».");

        // Load history ourselves — avoid racing the async PropertyChanged handler.
        _suppressProjectSelectionHandler = true;
        try
        {
            Projects.SelectedProject = project;
            SnapshotProjectsIntoSettings();
            try { await _settingsService.SaveAsync(Settings).ConfigureAwait(true); }
            catch (Exception ex) { _logService.LogWarn("[settings] save after inbound project switch: " + ex.Message); }

            _logService.SetActiveProject(project.Name);
            _projectAgentsBootstrap.EnsureProjectHermesArtifacts(project.WindowsPath);
            RefreshChatModeStatusUi();
            await LoadProjectHistoryAsync(project).ConfigureAwait(true);
            _ = PublishSessionContextToSupabaseIfChangedAsync("inbound-project-route");
        }
        finally
        {
            _suppressProjectSelectionHandler = false;
        }

        return true;
    }

    /// <summary>
    /// Recent remote chat rows for Hermes that were already in <c>messages</c> at Connect time.
    /// </summary>
    private bool ShouldCatchUpSupabaseInbound(SupabaseMessageRow m, DateTime floorInclusive)
    {
        if (m.CreatedAt < floorInclusive)
        {
            return false;
        }

        if (!MatchesInboundRecipientFilter(m))
        {
            return false;
        }

        if (IsHermesSenderRow(m, CanonicalHermesSenderName()))
        {
            return false;
        }

        if (IsOwnMirroredUserRow(m))
        {
            return false;
        }

        if (HermesWpfSessionContextPayload.IsSessionPayload(m.Content))
        {
            return false;
        }

        if (IsSupabaseServiceLogContent(m.Content))
        {
            return false;
        }

        if (IsHwtScreenshotContent(m.Content))
        {
            return false;
        }

        return true;
    }

    /// <summary>TaskerToWpf contract: <c>[LOG:…]</c> is not a user chat bubble.</summary>
    private static bool IsSupabaseServiceLogContent(string? content)
    {
        var t = (content ?? string.Empty).TrimStart();
        return t.StartsWith("[LOG:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHwtScreenshotContent(string? content)
    {
        var t = (content ?? string.Empty).TrimStart();
        if (t.Length < 10 || t[0] != '{')
        {
            return false;
        }

        return t.Contains("\"hwt_screenshot\"", StringComparison.OrdinalIgnoreCase)
               || t.Contains("\"hwt_screenshot_repeat\"", StringComparison.OrdinalIgnoreCase);
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
            _logService.LogError(
                "[supabase] URL or anon key empty; relay not started. "
                + "Заполните Settings → Supabase или сохраните credentials в %LocalAppData%\\DesktopVoiceChat\\settings.json и перезапустите Hermes.Wpf.");
            return;
        }

        _supabaseRelay = new SupabaseChatRelayService(_logService, Settings);
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
            _supabaseSeenPhotoNonces.Clear();

            if (Settings.SupabaseImportFullHistoryOnConnect)
            {
                foreach (var m in rows)
                {
                    _supabaseSeenMessageIds.Add(m.Id);
                    HydrateSupabaseSnapshotRowIntoChat(m);
                }

                var proj = Projects.SelectedProject;
                if (proj is not null)
                {
                    await SaveHistoryAsync(proj.Name);
                }
            }
            else
            {
                // Do not dump full history, but catch up recent inbound for Hermes
                // (messages already in the table when Connect runs — previously marked seen and dropped).
                var catchUpFloor = Settings.SupabaseUseLocalCreatedAt
                    ? DateTime.Now.AddMinutes(-5)
                    : DateTime.UtcNow.AddMinutes(-5);
                var catchUpCount = 0;
                foreach (var m in rows.OrderBy(x => x.CreatedAt))
                {
                    if (ShouldCatchUpSupabaseInbound(m, catchUpFloor))
                    {
                        await HandleInboundSupabaseRowAsync(m).ConfigureAwait(true);
                        catchUpCount++;
                    }

                    _supabaseSeenMessageIds.Add(m.Id);
                }

                if (catchUpCount > 0)
                {
                    _logService.LogInfo(
                        $"[supabase] Catch-up inbound after connect: processed={catchUpCount} (floor={catchUpFloor:O}).");
                }
            }

            StartSupabaseRealtime();

            if (Settings.EnableSupabasePoll)
            {
                _supabasePollingEnabled = true;
                StartSupabasePollTimer();
                _logService.LogInfo(
                    $"[supabase] Poll enabled (interval={ClampSupabasePollSeconds(Settings.SupabasePollIntervalSeconds)}s).");
            }
            else
            {
                _supabasePollingEnabled = false;
                _logService.LogInfo("[supabase] Poll disabled — inbound via WebSocket Realtime only.");
            }

            if (string.Equals(
                    LocalSenderDisplayName(),
                    CanonicalHermesSenderName(),
                    StringComparison.OrdinalIgnoreCase))
            {
                _logService.LogWarn(
                    "[supabase] Desktop sender_name и Assistant sender_name совпадают — inbound может зациклить агента. Задайте разные имена.");
            }

            var importMode = Settings.SupabaseImportFullHistoryOnConnect ? "full" : "catch-up-5m";
            var inbound = Settings.EnableSupabasePoll ? "realtime+poll" : "realtime";
            _logService.LogInfo(
                $"[supabase] Hermes relay: inbound={inbound}, snapshot rows={rows.Count}, import={importMode}.");

            RaiseSupabaseConnectionUi();
            await PublishAppStartupNotificationAsync("relay-connected").ConfigureAwait(true);
            await PublishSessionContextToSupabaseIfChangedAsync("relay-connected").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Connect failed: {ex.Message}");
            StopSupabaseRealtimeOnly();
            _supabaseRelay?.Disconnect();
            _supabaseRelay = null;
            _supabasePollingEnabled = false;
            RaiseSupabaseConnectionUi();
        }
    }

    private Task StopSupabaseRelayCoreAsync()
    {
        StopSupabasePollTimerOnly();
        StopSupabaseRealtimeOnly();
        _supabaseRelay?.Disconnect();
        _supabaseRelay = null;
        _supabasePollingEnabled = false;
        _supabaseSeenMessageIds.Clear();
        _supabaseSeenPhotoNonces.Clear();
        _lastPublishedSessionFingerprint = null;
        RaiseSupabaseConnectionUi();
        return Task.CompletedTask;
    }

    private void StartSupabaseRealtime()
    {
        StopSupabaseRealtimeOnly();
        _supabaseRealtime = new SupabaseRealtimeWebSocket(_logService);
        _supabaseRealtime.MessageInserted += OnSupabaseRealtimeMessageInserted;
        _supabaseRealtime.StatusChanged += status =>
            _logService.LogInfo($"[supabase] {status}");
        _supabaseRealtime.Start(Settings.SupabaseUrl.Trim(), Settings.SupabaseAnonKey.Trim());
    }

    private void StopSupabaseRealtimeOnly()
    {
        if (_supabaseRealtime is null)
        {
            return;
        }

        _supabaseRealtime.MessageInserted -= OnSupabaseRealtimeMessageInserted;
        _supabaseRealtime.Stop();
        _supabaseRealtime = null;
    }

    private void OnSupabaseRealtimeMessageInserted(SupabaseMessageRow row)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(async () => await HandleSupabaseRealtimeInboundAsync(row));
    }

    private async Task HandleSupabaseRealtimeInboundAsync(SupabaseMessageRow m)
    {
        if (!Settings.SupabaseRelayEnabled || _supabaseRelay is not { IsConnected: true })
        {
            return;
        }

        // HWT status / RemoteTerminal traffic is view-only — never treat as chat inbound.
        if (IsHwtStatusOrRemoteTerminalTraffic(m))
        {
            _ = _supabaseSeenMessageIds.Add(m.Id);
            return;
        }

        await _supabasePollGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!_supabaseSeenMessageIds.Add(m.Id))
            {
                return;
            }

            _logService.LogInfo(
                $"[supabase] Realtime received: {m.SenderName} → {m.RecipientName}, {(m.Content ?? string.Empty).Length} chars.");
            await HandleInboundSupabaseRowAsync(m).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Realtime inbound failed: {ex.Message}");
        }
        finally
        {
            _supabasePollGate.Release();
        }
    }

    private static bool IsHwtStatusOrRemoteTerminalTraffic(SupabaseMessageRow m)
    {
        var recipient = (m.RecipientName ?? string.Empty).Trim();
        if (string.Equals(recipient, "RemoteTerminal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var content = (m.Content ?? string.Empty).TrimStart();
        return content.StartsWith("{\"type\":\"hwt_status\"", StringComparison.OrdinalIgnoreCase)
               || content.StartsWith("{\"type\": \"hwt_status\"", StringComparison.OrdinalIgnoreCase);
    }

    private void HydrateSupabaseSnapshotRowIntoChat(SupabaseMessageRow m)
    {
        if (IsOwnMirroredUserRow(m))
        {
            return;
        }

        if (IsHwtStatusOrRemoteTerminalTraffic(m))
        {
            return;
        }

        if (HermesWpfSessionContextPayload.IsSessionPayload(m.Content))
        {
            return;
        }

        if (IsSupabaseServiceLogContent(m.Content))
        {
            return;
        }

        if (IsHwtScreenshotContent(m.Content))
        {
            return;
        }

        if (!MatchesInboundRecipientFilter(m))
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
        if (AndroidChatPhotoPayload.TryParse(m.Content, m.SenderName, out var photo))
        {
            Chat.Messages.Add(new ChatMessage
            {
                Role = "User",
                Text = $"{name}: 📷 {photo.Name}",
            });
            return;
        }

        Chat.Messages.Add(new ChatMessage { Role = "User", Text = $"{name}: {m.Content}" });
    }

    private void StartSupabasePollTimer()
    {
        StopSupabasePollTimerOnly();
        if (!Settings.EnableSupabasePoll)
        {
            return;
        }

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
            !Settings.EnableSupabasePoll ||
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
        if (IsHwtStatusOrRemoteTerminalTraffic(m))
        {
            return;
        }

        var canon = CanonicalHermesSenderName();
        if (IsHermesSenderRow(m, canon))
        {
            if (_supabaseEchoTracker.TryConsumeEcho(m, canon))
            {
                return;
            }

            if (HermesWpfSessionContextPayload.IsSessionPayload(m.Content))
            {
                return;
            }

            if (!MatchesInboundRecipientFilter(m))
            {
                return;
            }

            if (!await EnsureProjectContextForInboundRecipientAsync(m.RecipientName).ConfigureAwait(true))
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

        if (!MatchesInboundRecipientFilter(m))
        {
            return;
        }

        if (IsSupabaseServiceLogContent(m.Content))
        {
            _logService.LogInfo(
                $"[supabase] Skip service log from {(m.SenderName ?? "?").Trim()} (not shown as chat).");
            return;
        }

        if (IsHwtScreenshotContent(m.Content))
        {
            _logService.LogInfo("[supabase] Skip hwt_screenshot in Hermes chat (RemoteTerminal protocol).");
            return;
        }

        if (!await EnsureProjectContextForInboundRecipientAsync(m.RecipientName).ConfigureAwait(true))
        {
            return;
        }

        var remoteName = string.IsNullOrWhiteSpace(m.SenderName) ? "Remote" : m.SenderName.Trim();

        if (AndroidChatPhotoPayload.TryParse(m.Content, m.SenderName, out var photo))
        {
            await HandleInboundAndroidChatPhotoAsync(remoteName, photo).ConfigureAwait(true);
            return;
        }

        var payloadForAgent = string.IsNullOrEmpty((m.Content ?? string.Empty).Trim())
            ? "(пустое сообщение из Supabase)"
            : (m.Content ?? string.Empty).Trim();

        await HandleInboundRemoteUserMessageAsync(remoteName, payloadForAgent, source: "supabase")
            .ConfigureAwait(true);
    }

    private void OnWhatsAppMessageReceived(WhatsAppMessage msg)
    {
        DispatchToUi(() => _ = HandleInboundWhatsAppMessageAsync(msg));
    }

    private async Task HandleInboundWhatsAppMessageAsync(WhatsAppMessage msg)
    {
        var text = (msg.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        await HandleInboundRemoteUserMessageAsync(msg.FromName, text, source: "whatsapp").ConfigureAwait(true);
    }

    private async Task HandleInboundRemoteUserMessageAsync(
        string remoteName,
        string payloadForAgent,
        string source,
        string? uiBubbleLine = null,
        string? uiImagePath = null,
        IReadOnlyList<ChatAttachment>? uiAttachments = null)
    {
        // Route replies back to remote tutor client without mentioning it in agent system prompts.
        if (string.Equals(remoteName, "EnglishTutorClient", StringComparison.OrdinalIgnoreCase))
        {
            _supabaseDynamicOutboundRecipient = "EnglishTutorClient";
            _logService.LogInfo("[supabase] Dynamic outbound recipient → EnglishTutorClient");
        }

        var bubble = uiBubbleLine ?? $"{remoteName}: {payloadForAgent}";
        Chat.Messages.Add(new ChatMessage
        {
            Role = "User",
            Text = bubble,
            ImagePath = uiImagePath,
            Attachments = uiAttachments,
            AttachmentPaths = uiAttachments?.Select(a => a.FilePath).ToList(),
        });
        RequestChatScrollToBottom();

        if (Projects.SelectedProject is null)
        {
            _logService.LogWarn(
                $"[{source}] Входящее сообщение показано в чате; ответ Hermes не запущен — выберите проект.");
            return;
        }

        _chatLogService.AppendMessage(Projects.SelectedProject.Name, "User", bubble);
        await TrySaveHistoryAfterTurnAsync(Projects.SelectedProject.Name).ConfigureAwait(true);

        if (string.Equals(source, "whatsapp", StringComparison.OrdinalIgnoreCase)
            && !Settings.WhatsAppTriggerHermesAgent)
        {
            _whatsAppLogService.LogInfo("[whatsapp] Сообщение в чате; вызов Hermes отключён в настройках.");
            return;
        }

        await ExecuteHermesUserTurnAsync(
            prependUserBubble: false,
            agentUserPayload: payloadForAgent).ConfigureAwait(true);
    }

    private async Task HandleInboundAndroidChatPhotoAsync(string remoteName, AndroidChatPhotoPayload photo)
    {
        if (!string.IsNullOrWhiteSpace(photo.Nonce)
            && !_supabaseSeenPhotoNonces.Add(photo.Nonce))
        {
            _logService.LogInfo($"[supabase] Photo nonce already seen: {photo.Nonce}");
            return;
        }

        if (_supabaseRelay is not { IsConnected: true })
        {
            _logService.LogError("[supabase] Photo skipped: relay not connected (JSON not sent to agent).");
            return;
        }

        byte[]? bytes;
        try
        {
            bytes = await _supabaseRelay
                .DownloadStorageObjectAsync(photo.Bucket, photo.Path)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Photo download failed: {ex.Message}");
            AppendTerminal($"[supabase] Не удалось скачать фото {photo.Name}: {ex.Message}", isError: true);
            return;
        }

        if (bytes is null || bytes.Length == 0)
        {
            _logService.LogError($"[supabase] Photo download empty: {photo.Name} path={photo.Path}");
            AppendTerminal($"[supabase] Фото {photo.Name} не скачалось (Storage).", isError: true);
            return;
        }

        var project = Projects.SelectedProject;
        if (project is null)
        {
            _logService.LogWarn("[supabase] Photo downloaded but no project selected.");
            return;
        }

        ChatAttachment attachment;
        try
        {
            attachment = ChatAttachmentStore.ImportBytes(bytes, project.WindowsPath, photo.Name);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[supabase] Photo save failed: {ex.Message}");
            return;
        }

        var caption = $"photo from {remoteName}: {photo.Name}";
        var agentPayload = BuildChatAttachmentAgentPayload(caption, [attachment], _projectService);
        var bubble = $"{remoteName}: 📷 {photo.Name}";
        _logService.LogInfo(
            $"[supabase] Photo inbound {photo.Name} → {attachment.FilePath} ({attachment.SizeBytes} bytes)");

        await HandleInboundRemoteUserMessageAsync(
                remoteName,
                agentPayload,
                source: "supabase",
                uiBubbleLine: bubble,
                uiImagePath: attachment.IsImage ? attachment.FilePath : null,
                uiAttachments: [attachment])
            .ConfigureAwait(true);
    }

    private async Task StartWhatsAppWebCoreAsync()
    {
        if (!Settings.WhatsAppWebEnabled)
        {
            ApplyWhatsAppReadinessUi(WhatsAppMonitorReadiness.Off, string.Empty);
            return;
        }

        ApplyWhatsAppReadinessUi(WhatsAppMonitorReadiness.Starting, "Запуск WhatsApp Web…");

        await StopWhatsAppWebCoreAsync().ConfigureAwait(true);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        try
        {
            await dispatcher.InvokeAsync(async () =>
            {
                _whatsAppWindow = new WhatsAppWebWindow();
                _whatsAppWindow.Show();
                _whatsAppReader = new WhatsAppWebReader(_whatsAppWindow);
                _whatsAppMonitor = new WhatsAppWebMonitorService(_whatsAppLogService, _whatsAppReader);
                _whatsAppMonitor.MessageReceived += OnWhatsAppMessageReceived;
                _whatsAppMonitor.ReadinessChanged += OnWhatsAppReadinessChanged;
                _whatsAppMonitor.StatusChanged += line => AppendTerminal($"[whatsapp] {line}");
                await _whatsAppMonitor.InitializeAsync().ConfigureAwait(true);
                await _whatsAppMonitor.StartMonitoringAsync(
                    Settings.WhatsAppContactDisplayName,
                    Settings.WhatsAppPollIntervalMs,
                    Settings.GetEffectiveWhatsAppTextMarker(),
                    Settings.WhatsAppParseProbeEnabled,
                    Settings.GetEffectiveWhatsAppMinTextLength()).ConfigureAwait(true);
            }).Task.ConfigureAwait(true);

            _whatsAppLogService.LogInfo(
                $"[whatsapp] Relay active for «{Settings.WhatsAppContactDisplayName}» → Hermes chat (minText={Settings.GetEffectiveWhatsAppMinTextLength()}, allow1char={Settings.WhatsAppAllowSingleCharMessages})");
        }
        catch (Exception ex)
        {
            _whatsAppLogService.LogError($"[whatsapp] Start failed: {ex.Message}");
            ApplyWhatsAppReadinessUi(WhatsAppMonitorReadiness.Error, $"WhatsApp Web: {ex.Message}");
            await StopWhatsAppWebCoreAsync().ConfigureAwait(true);
        }
    }

    private void OnWhatsAppReadinessChanged(WhatsAppMonitorReadiness state, string message) =>
        DispatchToUi(() => ApplyWhatsAppReadinessUi(state, message));

    private void ApplyWhatsAppReadinessUi(WhatsAppMonitorReadiness state, string message)
    {
        _whatsAppReadiness = state;
        WhatsAppStatusText = message;
        RaisePropertyChanged(nameof(WhatsAppIndicatorState));
        RaisePropertyChanged(nameof(IsWhatsAppChatStatusVisible));
    }

    private void ShowWhatsAppWebWindow()
    {
        if (_whatsAppMonitor is not null)
        {
            _whatsAppMonitor.ShowWhatsAppWindow();
            return;
        }

        _whatsAppReader?.ShowWindow();
    }

    private async Task RunWhatsAppParseProbeManualAsync()
    {
        if (_whatsAppMonitor is null)
        {
            return;
        }

        try
        {
            var result = await _whatsAppMonitor.RunParseProbeAsync().ConfigureAwait(true);
            var line = result.Success
                ? $"Parse probe OK — {result.DetectLatencyMs} ms"
                : $"Parse probe FAILED — {result.FailureReason}";
            _whatsAppLogService.LogInfo($"[whatsapp] {line}");
            AppendTerminal($"[whatsapp] {line}");
            ApplyWhatsAppReadinessUi(
                result.Success ? WhatsAppMonitorReadiness.Ready : WhatsAppMonitorReadiness.Stalled,
                result.Success
                    ? $"Готов — парсинг проверен ({result.DetectLatencyMs} ms)"
                    : $"Парсинг не подтверждён: {result.FailureReason}");
        }
        catch (Exception ex)
        {
            _whatsAppLogService.LogWarn($"[whatsapp] Manual probe: {ex.Message}");
            ApplyWhatsAppReadinessUi(
                WhatsAppMonitorReadiness.Stalled,
                $"Ошибка проверки парсинга: {ex.Message}");
        }
    }

    private async Task StopWhatsAppWebCoreAsync()
    {
        if (_whatsAppMonitor is not null)
        {
            _whatsAppMonitor.MessageReceived -= OnWhatsAppMessageReceived;
            _whatsAppMonitor.ReadinessChanged -= OnWhatsAppReadinessChanged;
            _whatsAppMonitor.StopMonitoring();
            await _whatsAppMonitor.DisposeAsync().ConfigureAwait(true);
            _whatsAppMonitor = null;
        }

        _whatsAppReader = null;
        ApplyWhatsAppReadinessUi(WhatsAppMonitorReadiness.Off, string.Empty);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && _whatsAppWindow is not null)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (_whatsAppWindow is not null)
                {
                    _whatsAppWindow.ForceClose();
                }

                _whatsAppWindow = null;
            }).Task.ConfigureAwait(true);
        }
        else
        {
            _whatsAppWindow = null;
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
        PushHermesQuickCommandStatus();
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
                NotifyHermesCliFailure(
                    Projects.SelectedProject?.Name ?? "Hermes",
                    result,
                    hint);
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
            ClearHermesUiActivityTrackers();
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task SaveHistoryAsync(string projectName)
    {
        if (!string.IsNullOrWhiteSpace(_chatHistoryBoundProject)
            && !string.Equals(_chatHistoryBoundProject, projectName, StringComparison.OrdinalIgnoreCase))
        {
            _logService.LogWarn(
                $"[history] Skip save for «{projectName}»: chat UI is bound to «{_chatHistoryBoundProject}»");
            return;
        }

        await SaveHistorySnapshotAsync(projectName, Chat.Messages.ToList()).ConfigureAwait(true);
        _chatHistoryBoundProject = projectName;
    }

    private async Task SaveHistorySnapshotAsync(string projectName, List<ChatMessage> messages)
    {
        var session = new SessionHistory
        {
            ProjectName = projectName,
            Messages = messages,
            UpdatedAt = DateTime.Now
        };

        await _historyService.SaveAsync(session);
        _logService.LogInfo(
            $"[history] Persisted {session.Messages.Count} message(s) for «{projectName}» → {_historyService.GetHistoryFilePath(projectName)}");
        if (!SessionHistoryTitles.Contains(projectName))
        {
            SessionHistoryTitles.Add(projectName);
        }
    }

    private void OnHermesProcessOutputLine(string line)
    {
        AppendTerminal(line);
        MaybeUpgradeAgentActivityFromHermesStream(line);
    }

    private void MaybeUpgradeAgentActivityFromHermesStream(string line)
    {
        if (!_agentActivityTracking || _agentActivityAssumeExecuting || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (!HermesStreamLikelyToolActivity.IsMatch(line))
        {
            return;
        }

        DispatchToUi(UpgradeAgentHermesActivityToExecuting);
    }

    private void DispatchToUi(Action action)
    {
        var app = Application.Current;
        var d = app?.Dispatcher;
        if (d is null || d.CheckAccess())
        {
            action();
        }
        else
        {
            _ = d.InvokeAsync(action);
        }
    }

    private void RaiseSupabaseConnectionUi()
    {
        RaisePropertyChanged(nameof(SupabaseIndicatorState));
        RaisePropertyChanged(nameof(SupabaseRelayStatusText));
        RaisePropertyChanged(nameof(SupabaseRelayToggleCaption));
        RaisePropertyChanged(nameof(SupabaseRelayToggleHint));
        RaisePropertyChanged(nameof(SupabaseRelayToolTipShort));
    }

    private async Task ToggleSupabaseRelayConnectionAsync()
    {
        if (_supabaseRelayToggleBusy)
        {
            return;
        }

        _supabaseRelayToggleBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (_supabaseRelay?.IsConnected == true)
            {
                await StopSupabaseRelayCoreAsync();
                if (Settings.SupabaseRelayEnabled)
                {
                    Settings.SupabaseRelayEnabled = false;
                    _ = PersistSettingsQuietAsync();
                }

                _logService.LogInfo("[supabase] Relay отключён с панели (клиент закрыт).");
            }
            else
            {
                if (!Settings.SupabaseRelayEnabled)
                {
                    Settings.SupabaseRelayEnabled = true;
                    _ = PersistSettingsQuietAsync();
                }

                await StartSupabaseRelayCoreAsync();
            }
        }
        finally
        {
            _supabaseRelayToggleBusy = false;
            RaiseSupabaseConnectionUi();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void PushHermesThinkingStatus()
    {
        _agentActivityTracking = true;
        _agentActivityAssumeExecuting = false;
        AgentChatStatusLine = "Hermes думает (формирует ответ)…";
        IsAgentChatStatusBarVisible = true;
    }

    private void PushHermesQuickCommandStatus()
    {
        _agentActivityTracking = true;
        _agentActivityAssumeExecuting = true;
        AgentChatStatusLine = "Hermes выполняет команду CLI…";
        IsAgentChatStatusBarVisible = true;
    }

    private void UpgradeAgentHermesActivityToExecuting()
    {
        if (!_agentActivityTracking || _agentActivityAssumeExecuting)
        {
            return;
        }

        AgentChatStatusLine = "Hermes выполняет действие (инструмент или команда)…";
    }

    private void ClearHermesUiActivityTrackers()
    {
        _agentActivityTracking = false;
        _agentActivityAssumeExecuting = false;
        AgentChatStatusLine = string.Empty;
        IsAgentChatStatusBarVisible = false;
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

        try
        {
            var ws = Projects.SelectedProject?.Name ?? "(none)";
            var kind = isError ? "error" : "terminal";
            _activityBus.Publish(ws, kind, text, isError);
        }
        catch
        {
            // activity bus must not break chat
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

        static string? PreferMeaningful(string? blob)
        {
            if (string.IsNullOrWhiteSpace(blob))
                return null;

            var lines = blob.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var cue in new[]
                     {
                         "Key limit exceeded",
                         "API call failed",
                         "HTTP 403",
                         "HTTP 401",
                         "HTTP 429",
                         "rate limit",
                         "Failed to start the systemd",
                     })
            {
                var hit = lines.LastOrDefault(l => l.Contains(cue, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(hit))
                    return hit;
            }

            return null;
        }

        var preferred = PreferMeaningful(result.CombinedText) ?? PreferMeaningful(result.LastStderrLine);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var line = preferred.Trim();
            if (line.Length > 400)
                line = line[..400] + "…";
            return AppendTimeoutHint(line);
        }

        if (!string.IsNullOrWhiteSpace(result.LastStderrLine))
        {
            var line = result.LastStderrLine.Trim();
            if (line.StartsWith("session_id:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(result.CombinedText))
            {
                // Fall through to CombinedText — session_id alone is not useful.
            }
            else
            {
                if (line.Length > 400)
                    line = line[..400] + "…";
                return AppendTimeoutHint(line);
            }
        }

        var combined = result.CombinedText.Trim();
        if (string.IsNullOrEmpty(combined))
        {
            return AppendTimeoutHint(result.ExitCode == -1 ? "Превышено время ожидания Hermes." : "(нет вывода)");
        }

        var all = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = all.Length > 0 ? all[^1] : combined;
        if (last.StartsWith("session_id:", StringComparison.OrdinalIgnoreCase) && all.Length >= 2)
            last = all[^2];
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
