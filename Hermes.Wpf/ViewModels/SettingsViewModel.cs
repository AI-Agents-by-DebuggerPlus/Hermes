using System.Globalization;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly HermesSettings _settings;
    private string _wslDistro;
    private string _venvPath;
    private string _hermesCommand;
    private int _chatTimeoutSeconds;
    /// <summary>Committed value backing <see cref="HermesSettings.ChatFontSize"/>.</summary>
    private double _chatFontSizeCommitted;

    private string _chatFontSizeEdit = string.Empty;
    private bool _autoReconnect;
    private bool _diagnosticLogHermesCommands;
    private bool _appendVisionScopeReminder;
    private string _visionScopeReminderNote;
    private string _workspaceRootWindowsPath;
    private string _lastWorkspaceBrowsePath;
    private bool _supabaseRelayEnabled;
    private bool _hermesAgentPaused;
    private string _supabaseUrl = string.Empty;
    private string _supabaseAnonKey = string.Empty;
    private bool _supabaseUseLocalCreatedAt;
    private int _supabasePollIntervalSeconds;
    private bool _supabaseUseAnonymousAuth;
    private bool _supabaseImportFullHistoryOnConnect;
    private string _supabaseHermesSenderName = "Hermes";
    private string _supabaseLocalSenderName = "Desktop";
    private string _externalBrainMemoryPath = string.Empty;
    private bool _externalBrainInjectIntoPrompt = true;
    private bool _externalBrainVectorRetrievalEnabled = true;
    private bool _externalBrainUseOllamaEmbeddings = true;
    private string _externalBrainOllamaBaseUrl = "http://127.0.0.1:11434";
    private string _externalBrainEmbeddingModel = "nomic-embed-text";
    private bool _skillGenerationEnabled = true;
    private bool _skillMirrorToWslHermes = true;
    private bool _skillRunTestsBeforeSave = true;
    private bool _skillSandboxBeforeSave = true;
    private bool _skillAutoResolveForTasks = true;
    private string _generatedSkillsDirectory = string.Empty;
    private bool _syncWslAgentMemoryToExternalBrain = true;
    private int _externalBrainMaxContextItems = 10;
    private string _externalBrainMaxContextEdit = "10";
    private bool _desktopMouseSkillEnabled;
    private bool _desktopVisionAnalyzeEnabled = true;
    private bool _desktopVisionUseAnnotatedImage = true;
    private string _desktopScreenshotDirectory = string.Empty;
    private string _desktopScreenshotMonitorIndexEdit = "-1";
    private string _lastScreenshotBrowsePath = string.Empty;

    public SettingsViewModel(HermesSettings settings)
    {
        _settings = settings;
        _wslDistro = settings.WslDistro;
        _venvPath = settings.VenvPath;
        _hermesCommand = settings.HermesCommand;
        _chatTimeoutSeconds = settings.ChatTimeoutSeconds;
        _chatFontSizeCommitted = ClampChatFont(settings.ChatFontSize);
        settings.ChatFontSize = _chatFontSizeCommitted;
        _chatFontSizeEdit = FormatChatFontEdit(_chatFontSizeCommitted);
        _autoReconnect = settings.AutoReconnect;
        _diagnosticLogHermesCommands = settings.DiagnosticLogHermesCommands;
        _appendVisionScopeReminder = settings.AppendVisionScopeReminder;
        _visionScopeReminderNote = settings.VisionScopeReminderNote ?? string.Empty;
        _workspaceRootWindowsPath = settings.WorkspaceRootWindowsPath ?? string.Empty;
        _lastWorkspaceBrowsePath = settings.LastWorkspaceBrowsePath ?? string.Empty;
        _supabaseRelayEnabled = settings.SupabaseRelayEnabled;
        _hermesAgentPaused = settings.HermesAgentPaused;
        _supabaseUrl = settings.SupabaseUrl ?? string.Empty;
        _supabaseAnonKey = settings.SupabaseAnonKey ?? string.Empty;
        _supabaseUseLocalCreatedAt = settings.SupabaseUseLocalCreatedAt;
        _supabasePollIntervalSeconds = ClampPollInterval(settings.SupabasePollIntervalSeconds);
        _supabaseUseAnonymousAuth = settings.SupabaseUseAnonymousAuth;
        _supabaseImportFullHistoryOnConnect = settings.SupabaseImportFullHistoryOnConnect;
        _supabaseHermesSenderName = string.IsNullOrWhiteSpace(settings.SupabaseHermesSenderName)
            ? "Hermes"
            : settings.SupabaseHermesSenderName.Trim();
        _supabaseLocalSenderName = string.IsNullOrWhiteSpace(settings.SupabaseLocalSenderName)
            ? "Desktop"
            : settings.SupabaseLocalSenderName.Trim();
        _externalBrainMemoryPath = settings.ExternalBrainMemoryPath ?? string.Empty;
        _externalBrainInjectIntoPrompt = settings.ExternalBrainInjectIntoPrompt;
        _externalBrainVectorRetrievalEnabled = settings.ExternalBrainVectorRetrievalEnabled;
        _externalBrainUseOllamaEmbeddings = settings.ExternalBrainUseOllamaEmbeddings;
        _externalBrainOllamaBaseUrl = string.IsNullOrWhiteSpace(settings.ExternalBrainOllamaBaseUrl)
            ? "http://127.0.0.1:11434"
            : settings.ExternalBrainOllamaBaseUrl.Trim();
        _externalBrainEmbeddingModel = string.IsNullOrWhiteSpace(settings.ExternalBrainEmbeddingModel)
            ? "nomic-embed-text"
            : settings.ExternalBrainEmbeddingModel.Trim();
        _skillGenerationEnabled = settings.SkillGenerationEnabled;
        _skillMirrorToWslHermes = settings.SkillMirrorToWslHermes;
        _skillRunTestsBeforeSave = settings.SkillRunTestsBeforeSave;
        _skillSandboxBeforeSave = settings.SkillSandboxBeforeSave;
        _skillAutoResolveForTasks = settings.SkillAutoResolveForTasks;
        _generatedSkillsDirectory = settings.GeneratedSkillsDirectory ?? string.Empty;
        _syncWslAgentMemoryToExternalBrain = settings.SyncWslAgentMemoryToExternalBrain;
        _externalBrainMaxContextItems = Math.Clamp(settings.ExternalBrainMaxContextItems, 1, 20);
        _externalBrainMaxContextEdit = _externalBrainMaxContextItems.ToString(CultureInfo.InvariantCulture);
        _desktopScreenshotDirectory = settings.DesktopScreenshotDirectory ?? string.Empty;
        _desktopScreenshotMonitorIndexEdit = settings.DesktopScreenshotMonitorIndex.ToString(CultureInfo.InvariantCulture);
        _desktopMouseSkillEnabled = settings.DesktopMouseSkillEnabled;
        _desktopVisionAnalyzeEnabled = settings.DesktopVisionAnalyzeEnabled;
        _desktopVisionUseAnnotatedImage = settings.DesktopVisionUseAnnotatedImage;
    }

    private static int ClampPollInterval(int seconds)
    {
        if (seconds < 1)
        {
            return 1;
        }

        return seconds > 120 ? 120 : seconds;
    }

    public string WslDistro
    {
        get => _wslDistro;
        set
        {
            _settings.WslDistro = value;
            SetProperty(ref _wslDistro, value);
        }
    }

    public string VenvPath
    {
        get => _venvPath;
        set
        {
            _settings.VenvPath = value;
            SetProperty(ref _venvPath, value);
        }
    }

    public string HermesCommand
    {
        get => _hermesCommand;
        set
        {
            _settings.HermesCommand = value;
            SetProperty(ref _hermesCommand, value);
        }
    }

    public int ChatTimeoutSeconds
    {
        get => _chatTimeoutSeconds;
        set
        {
            _settings.ChatTimeoutSeconds = value;
            SetProperty(ref _chatTimeoutSeconds, value);
        }
    }

    /// <summary>Editable text for chat font box; parse and clamp in <see cref="CommitChatFontSize"/>.</summary>
    public string ChatFontSizeEdit
    {
        get => _chatFontSizeEdit;
        set => SetProperty(ref _chatFontSizeEdit, value ?? string.Empty);
    }

    /// <summary>Call from Settings window when the chat font TextBox loses focus (avoids per-keystroke clamp).</summary>
    public void CommitChatFontSize()
    {
        var raw = _chatFontSizeEdit?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            var fallback = ClampChatFont(14);
            ApplyCommittedChatFontSize(fallback);
            return;
        }

        if (!TryParseFlexible(raw, out var parsed))
        {
            SyncEditTextFromCommitted();
            return;
        }

        ApplyCommittedChatFontSize(ClampChatFont(parsed));
    }

    private void ApplyCommittedChatFontSize(double c)
    {
        _chatFontSizeCommitted = c;
        _settings.ChatFontSize = c;
        var formatted = FormatChatFontEdit(c);
        SetProperty(ref _chatFontSizeEdit, formatted, nameof(ChatFontSizeEdit));
    }

    private void SyncEditTextFromCommitted() =>
        SetProperty(ref _chatFontSizeEdit, FormatChatFontEdit(_chatFontSizeCommitted), nameof(ChatFontSizeEdit));

    private static string FormatChatFontEdit(double c) =>
        Math.Abs(c - Math.Round(c)) < 0.0001
            ? Math.Round(c).ToString("0", CultureInfo.InvariantCulture)
            : c.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryParseFlexible(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture,
                out value))
        {
            return true;
        }

        return double.TryParse(raw.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            _settings.AutoReconnect = value;
            SetProperty(ref _autoReconnect, value);
        }
    }

    public bool DiagnosticLogHermesCommands
    {
        get => _diagnosticLogHermesCommands;
        set
        {
            _settings.DiagnosticLogHermesCommands = value;
            SetProperty(ref _diagnosticLogHermesCommands, value);
        }
    }

    public bool AppendVisionScopeReminder
    {
        get => _appendVisionScopeReminder;
        set
        {
            _settings.AppendVisionScopeReminder = value;
            SetProperty(ref _appendVisionScopeReminder, value);
        }
    }

    public string VisionScopeReminderNote
    {
        get => _visionScopeReminderNote;
        set
        {
            _settings.VisionScopeReminderNote = value ?? string.Empty;
            SetProperty(ref _visionScopeReminderNote, value ?? string.Empty);
        }
    }

    public string WorkspaceRootWindowsPath
    {
        get => _workspaceRootWindowsPath;
        set
        {
            var v = value ?? string.Empty;
            _settings.WorkspaceRootWindowsPath = v;
            SetProperty(ref _workspaceRootWindowsPath, v);
        }
    }

    public string LastWorkspaceBrowsePath
    {
        get => _lastWorkspaceBrowsePath;
        set
        {
            var v = value ?? string.Empty;
            _settings.LastWorkspaceBrowsePath = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            SetProperty(ref _lastWorkspaceBrowsePath, v);
        }
    }

    public bool SupabaseRelayEnabled
    {
        get => _supabaseRelayEnabled;
        set
        {
            _settings.SupabaseRelayEnabled = value;
            SetProperty(ref _supabaseRelayEnabled, value);
        }
    }

    public bool HermesAgentPaused
    {
        get => _hermesAgentPaused;
        set
        {
            _settings.HermesAgentPaused = value;
            SetProperty(ref _hermesAgentPaused, value);
        }
    }

    public string SupabaseUrl
    {
        get => _supabaseUrl;
        set
        {
            var v = value ?? string.Empty;
            _settings.SupabaseUrl = v;
            SetProperty(ref _supabaseUrl, v);
        }
    }

    public string SupabaseAnonKey
    {
        get => _supabaseAnonKey;
        set
        {
            var v = value ?? string.Empty;
            _settings.SupabaseAnonKey = v;
            SetProperty(ref _supabaseAnonKey, v);
        }
    }

    public bool SupabaseUseLocalCreatedAt
    {
        get => _supabaseUseLocalCreatedAt;
        set
        {
            _settings.SupabaseUseLocalCreatedAt = value;
            SetProperty(ref _supabaseUseLocalCreatedAt, value);
        }
    }

    public int SupabasePollIntervalSeconds
    {
        get => _supabasePollIntervalSeconds;
        set
        {
            var c = ClampPollInterval(value);
            _settings.SupabasePollIntervalSeconds = c;
            SetProperty(ref _supabasePollIntervalSeconds, c);
        }
    }

    public bool SupabaseUseAnonymousAuth
    {
        get => _supabaseUseAnonymousAuth;
        set
        {
            _settings.SupabaseUseAnonymousAuth = value;
            SetProperty(ref _supabaseUseAnonymousAuth, value);
        }
    }

    public bool SupabaseImportFullHistoryOnConnect
    {
        get => _supabaseImportFullHistoryOnConnect;
        set
        {
            _settings.SupabaseImportFullHistoryOnConnect = value;
            SetProperty(ref _supabaseImportFullHistoryOnConnect, value);
        }
    }

    public string SupabaseHermesSenderName
    {
        get => _supabaseHermesSenderName;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Hermes" : value.Trim();
            _settings.SupabaseHermesSenderName = v;
            SetProperty(ref _supabaseHermesSenderName, v);
        }
    }

    public string SupabaseLocalSenderName
    {
        get => _supabaseLocalSenderName;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Desktop" : value.Trim();
            _settings.SupabaseLocalSenderName = v;
            SetProperty(ref _supabaseLocalSenderName, v);
        }
    }

    public string ExternalBrainMemoryPath
    {
        get => _externalBrainMemoryPath;
        set
        {
            var v = value ?? string.Empty;
            _settings.ExternalBrainMemoryPath = v;
            SetProperty(ref _externalBrainMemoryPath, v);
        }
    }

    public bool ExternalBrainInjectIntoPrompt
    {
        get => _externalBrainInjectIntoPrompt;
        set
        {
            _settings.ExternalBrainInjectIntoPrompt = value;
            SetProperty(ref _externalBrainInjectIntoPrompt, value);
        }
    }

    public bool ExternalBrainVectorRetrievalEnabled
    {
        get => _externalBrainVectorRetrievalEnabled;
        set
        {
            _settings.ExternalBrainVectorRetrievalEnabled = value;
            SetProperty(ref _externalBrainVectorRetrievalEnabled, value);
        }
    }

    public bool ExternalBrainUseOllamaEmbeddings
    {
        get => _externalBrainUseOllamaEmbeddings;
        set
        {
            _settings.ExternalBrainUseOllamaEmbeddings = value;
            SetProperty(ref _externalBrainUseOllamaEmbeddings, value);
        }
    }

    public string ExternalBrainOllamaBaseUrl
    {
        get => _externalBrainOllamaBaseUrl;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:11434" : value.Trim();
            _settings.ExternalBrainOllamaBaseUrl = v;
            SetProperty(ref _externalBrainOllamaBaseUrl, v);
        }
    }

    public string ExternalBrainEmbeddingModel
    {
        get => _externalBrainEmbeddingModel;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "nomic-embed-text" : value.Trim();
            _settings.ExternalBrainEmbeddingModel = v;
            SetProperty(ref _externalBrainEmbeddingModel, v);
        }
    }

    public bool SkillGenerationEnabled
    {
        get => _skillGenerationEnabled;
        set
        {
            _settings.SkillGenerationEnabled = value;
            SetProperty(ref _skillGenerationEnabled, value);
        }
    }

    public bool SkillMirrorToWslHermes
    {
        get => _skillMirrorToWslHermes;
        set
        {
            _settings.SkillMirrorToWslHermes = value;
            SetProperty(ref _skillMirrorToWslHermes, value);
        }
    }

    public bool SkillRunTestsBeforeSave
    {
        get => _skillRunTestsBeforeSave;
        set
        {
            _settings.SkillRunTestsBeforeSave = value;
            SetProperty(ref _skillRunTestsBeforeSave, value);
        }
    }

    public bool SkillSandboxBeforeSave
    {
        get => _skillSandboxBeforeSave;
        set
        {
            _settings.SkillSandboxBeforeSave = value;
            SetProperty(ref _skillSandboxBeforeSave, value);
        }
    }

    public bool SkillAutoResolveForTasks
    {
        get => _skillAutoResolveForTasks;
        set
        {
            _settings.SkillAutoResolveForTasks = value;
            SetProperty(ref _skillAutoResolveForTasks, value);
        }
    }

    public string GeneratedSkillsDirectory
    {
        get => _generatedSkillsDirectory;
        set
        {
            var v = value ?? string.Empty;
            _settings.GeneratedSkillsDirectory = v;
            SetProperty(ref _generatedSkillsDirectory, v);
        }
    }

    public bool SyncWslAgentMemoryToExternalBrain
    {
        get => _syncWslAgentMemoryToExternalBrain;
        set
        {
            _settings.SyncWslAgentMemoryToExternalBrain = value;
            SetProperty(ref _syncWslAgentMemoryToExternalBrain, value);
        }
    }

    public bool DesktopMouseSkillEnabled
    {
        get => _desktopMouseSkillEnabled;
        set
        {
            _settings.DesktopMouseSkillEnabled = value;
            SetProperty(ref _desktopMouseSkillEnabled, value);
        }
    }

    public bool DesktopVisionAnalyzeEnabled
    {
        get => _desktopVisionAnalyzeEnabled;
        set
        {
            _settings.DesktopVisionAnalyzeEnabled = value;
            SetProperty(ref _desktopVisionAnalyzeEnabled, value);
        }
    }

    public bool DesktopVisionUseAnnotatedImage
    {
        get => _desktopVisionUseAnnotatedImage;
        set
        {
            _settings.DesktopVisionUseAnnotatedImage = value;
            SetProperty(ref _desktopVisionUseAnnotatedImage, value);
        }
    }

    public string DesktopScreenshotDirectory
    {
        get => _desktopScreenshotDirectory;
        set
        {
            var v = value ?? string.Empty;
            _settings.DesktopScreenshotDirectory = v;
            SetProperty(ref _desktopScreenshotDirectory, v);
        }
    }

    public string LastScreenshotBrowsePath
    {
        get => _lastScreenshotBrowsePath;
        set => SetProperty(ref _lastScreenshotBrowsePath, value ?? string.Empty);
    }

    public string DesktopScreenshotMonitorIndexEdit
    {
        get => _desktopScreenshotMonitorIndexEdit;
        set
        {
            _desktopScreenshotMonitorIndexEdit = value ?? string.Empty;
            RaisePropertyChanged(nameof(DesktopScreenshotMonitorIndexEdit));
            CommitScreenshotMonitorIndexIfParsed();
        }
    }

    public void NormalizeScreenshotMonitorIndexField()
    {
        if (!int.TryParse(_desktopScreenshotMonitorIndexEdit.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            _desktopScreenshotMonitorIndexEdit = _settings.DesktopScreenshotMonitorIndex.ToString(CultureInfo.InvariantCulture);
            RaisePropertyChanged(nameof(DesktopScreenshotMonitorIndexEdit));
            return;
        }

        CommitScreenshotMonitorIndexIfParsed();
    }

    private void CommitScreenshotMonitorIndexIfParsed()
    {
        if (!int.TryParse(_desktopScreenshotMonitorIndexEdit.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return;
        }

        _settings.DesktopScreenshotMonitorIndex = n;
        _desktopScreenshotMonitorIndexEdit = n.ToString(CultureInfo.InvariantCulture);
        RaisePropertyChanged(nameof(DesktopScreenshotMonitorIndexEdit));
    }

    public string ExternalBrainMaxContextItemsEdit
    {
        get => _externalBrainMaxContextEdit;
        set
        {
            _externalBrainMaxContextEdit = value ?? string.Empty;
            RaisePropertyChanged(nameof(ExternalBrainMaxContextItemsEdit));
            CommitExternalBrainMaxIfParsed();
        }
    }

    public void NormalizeExternalBrainMaxEditField()
    {
        if (!int.TryParse(_externalBrainMaxContextEdit.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            _externalBrainMaxContextEdit = _externalBrainMaxContextItems.ToString(CultureInfo.InvariantCulture);
            RaisePropertyChanged(nameof(ExternalBrainMaxContextItemsEdit));
            return;
        }

        CommitExternalBrainMaxIfParsed();
    }

    private void CommitExternalBrainMaxIfParsed()
    {
        if (!int.TryParse(_externalBrainMaxContextEdit.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return;
        }

        n = Math.Clamp(n, 1, 20);
        _externalBrainMaxContextItems = n;
        _settings.ExternalBrainMaxContextItems = n;
        _externalBrainMaxContextEdit = n.ToString(CultureInfo.InvariantCulture);
        RaisePropertyChanged(nameof(ExternalBrainMaxContextItemsEdit));
    }

    private static double ClampChatFont(double value)
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
}
