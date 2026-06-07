using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

/// <summary>Standalone Supabase <c>messages</c> table probe — no Hermes CLI.</summary>
public sealed class SupabaseTestChatViewModel : BaseViewModel
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private SupabaseChatRelayService? _relay;
    private readonly HashSet<Guid> _seenIds = [];
    private readonly DispatcherTimer _pollTimer;
    private readonly EventHandler _pollTickHandler;
    private string _urlEdit = string.Empty;
    private string _anonKeyEdit = string.Empty;
    private bool _useAnonymousAuth = true;
    private string _testSenderName = "HermesTest";
    private string _testRecipientName = "Hermes";
    private string _draft = string.Empty;
    private string _status = "Отключено. Укажите URL и anon key, затем «Подключиться».";
    private bool _connectBusy;

    public SupabaseTestChatViewModel(HermesSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _urlEdit = settings.SupabaseUrl ?? string.Empty;
        _anonKeyEdit = settings.SupabaseAnonKey ?? string.Empty;
        _useAnonymousAuth = settings.SupabaseUseAnonymousAuth;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _pollTickHandler = (_, _) => PollOnceFireAndForget();
        _pollTimer.Tick += _pollTickHandler;

        ConnectCommand = new RelayCommand(_ => _ = ConnectAsync(), _ => CanConnect());
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => _relay is { IsConnected: true } && !_connectBusy);
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend());
        RefreshCommand = new RelayCommand(_ => _ = PullAllAsync(), _ => _relay is { IsConnected: true } && !_connectBusy);
        SyncToSettingsCommand = new RelayCommand(_ => SyncToSettings());
    }

    public ObservableCollection<string> Lines { get; } = [];

    public string SupabaseUrlEdit
    {
        get => _urlEdit;
        set => SetProperty(ref _urlEdit, value ?? string.Empty);
    }

    public string SupabaseAnonKeyEdit
    {
        get => _anonKeyEdit;
        set => SetProperty(ref _anonKeyEdit, value ?? string.Empty);
    }

    public bool UseAnonymousAuth
    {
        get => _useAnonymousAuth;
        set => SetProperty(ref _useAnonymousAuth, value);
    }

    public string TestSenderName
    {
        get => _testSenderName;
        set => SetProperty(ref _testSenderName, string.IsNullOrWhiteSpace(value) ? "HermesTest" : value.Trim());
    }

    public string TestRecipientName
    {
        get => _testRecipientName;
        set => SetProperty(ref _testRecipientName, string.IsNullOrWhiteSpace(value) ? "Hermes" : value.Trim());
    }

    public string DraftMessage
    {
        get => _draft;
        set => SetProperty(ref _draft, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SyncToSettingsCommand { get; }

    /// <summary>Copies URL/key/anonymous flag into <see cref="HermesSettings"/> (caller may persist).</summary>
    public void SyncToSettings()
    {
        _settings.SupabaseUrl = SupabaseUrlEdit.Trim();
        _settings.SupabaseAnonKey = SupabaseAnonKeyEdit.Trim();
        _settings.SupabaseUseAnonymousAuth = UseAnonymousAuth;
    }

    public void Shutdown()
    {
        _pollTimer.Stop();
        _pollTimer.Tick -= _pollTickHandler;
        _relay?.Disconnect();
        _relay = null;
        _seenIds.Clear();
        Lines.Clear();
        Status = "Окно закрыто.";
    }

    private void PollOnceFireAndForget() => _ = PollOnceAsync();

    private bool CanConnect() =>
        !_connectBusy
        && _relay is not { IsConnected: true }
        && !string.IsNullOrWhiteSpace(SupabaseUrlEdit)
        && !string.IsNullOrWhiteSpace(SupabaseAnonKeyEdit);

    private bool CanSend() =>
        _relay is { IsConnected: true }
        && !_connectBusy
        && !string.IsNullOrWhiteSpace(DraftMessage.Trim());

    private async Task ConnectAsync()
    {
        _connectBusy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Подключение…";
        _log.LogInfo("[supabase-test] Connect requested (Hermes CLI not used).");

        try
        {
            _relay?.Disconnect();
            _relay = new SupabaseChatRelayService(_log, _settings);
            await _relay.ConnectAsync(SupabaseUrlEdit.Trim(), SupabaseAnonKeyEdit.Trim());
            if (UseAnonymousAuth)
            {
                await _relay.EnsureAnonymousSessionAsync();
            }

            SyncToSettings();
            _seenIds.Clear();
            Lines.Clear();
            await PullAllAsync(isInitial: true);
            _pollTimer.Start();
            Status = $"Подключено. user≈{ShortId(_relay.CurrentUserId)}. Опрос каждые 4 с.";
            _log.LogInfo("[supabase-test] Connect finished; poll timer started.");
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
            _log.LogError($"[supabase-test] Connect failed: {ex.Message}");
            _relay?.Disconnect();
            _relay = null;
        }
        finally
        {
            _connectBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void Disconnect()
    {
        _pollTimer.Stop();
        _relay?.Disconnect();
        _relay = null;
        _seenIds.Clear();
        Status = "Отключено.";
        _log.LogInfo("[supabase-test] Disconnected by user.");
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SendAsync()
    {
        if (_relay is not { IsConnected: true })
        {
            return;
        }

        var text = DraftMessage.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            _log.LogInfo($"[supabase-test] Sending row sender_name={TestSenderName}, chars={text.Length}");
            await _relay.InsertAssistantRowAsync(TestSenderName, TestRecipientName, text, CancellationToken.None, logPublish: false);
            DraftMessage = string.Empty;
            await PullAllAsync(isInitial: false);
        }
        catch (Exception ex)
        {
            Status = $"Отправка не удалась: {ex.Message}";
            _log.LogError($"[supabase-test] Send failed: {ex.Message}");
        }
    }

    private async Task PullAllAsync(bool isInitial = false)
    {
        if (_relay is not { IsConnected: true })
        {
            return;
        }

        try
        {
            var rows = await _relay.FetchAllSortedAsync();
            foreach (var m in rows.OrderBy(r => r.CreatedAt))
            {
                if (!_seenIds.Add(m.Id))
                {
                    continue;
                }

                var who = string.IsNullOrWhiteSpace(m.SenderName) ? "?" : m.SenderName.Trim();
                var to = string.IsNullOrWhiteSpace(m.RecipientName) ? "?" : m.RecipientName.Trim();
                var line =
                    $"{m.CreatedAt:yyyy-MM-dd HH:mm:ss} [{who} → {to}] {m.Content?.ReplaceLineEndings(" ") ?? string.Empty}";
                Lines.Add(line);
            }

            if (isInitial)
            {
                _log.LogInfo($"[supabase-test] Initial pull: rows={rows.Count}, new lines={Lines.Count}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[supabase-test] Pull failed: {ex.Message}");
        }
    }

    private async Task PollOnceAsync()
    {
        if (_relay is not { IsConnected: true })
        {
            return;
        }

        await PullAllAsync(isInitial: false);
    }

    private static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "?";
        }

        return id.Length <= 8 ? id : id[..8] + "…";
    }
}
