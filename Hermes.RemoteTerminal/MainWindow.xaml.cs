using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Hermes.RemoteTerminal.Models;
using Hermes.RemoteTerminal.Services;

namespace Hermes.RemoteTerminal;

public partial class MainWindow : Window
{
    private AppSettings _settings = SettingsStore.Load();
    private SupabaseRealtimeMessages? _realtime;
    private SupabaseRestBackupBridge? _restBackup;
    private SupabaseSessionClient? _session;
    private LogWindow? _logWindow;
    private ScreenshotViewerWindow? _shotViewer;
    private int _lineCount;
    private bool _mirroringLog;
    private bool _pollBusy;
    private readonly HashSet<string> _seenShotKeys = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _seenMessageIds = new();
    private readonly HashSet<string> _seenFeedKeys = new(StringComparer.Ordinal);
    private readonly object _feedDedupGate = new();
    private DateTimeOffset _wsOfflineSince = DateTimeOffset.MinValue;
    private static readonly TimeSpan RestBackupGrace = TimeSpan.FromSeconds(15);
    private System.Windows.Threading.DispatcherTimer? _hwtRefreshRetry;
    private bool _hwtStatusReceived;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppVersion.WindowTitle;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        Title = AppVersion.WindowTitle;
        ApplyTheme();
        MirrorLogsCheck.IsChecked = _settings.MirrorLocalLogsToSupabase;
        AppendLocal($"RemoteTerminal {AppVersion.Display} ready · WebSocket + REST backup (XP) + Poll/F5 · settings: {SettingsStore.SettingsPath}");
        AppLog.LineAdded += OnAppLogLine;
        await StartRealtimeAsync().ConfigureAwait(true);
        StartBackupBridgeMonitor();
    }

    private async void Window_OnClosed(object? sender, EventArgs e)
    {
        AppLog.LineAdded -= OnAppLogLine;
        PreviewKeyDown -= MainWindow_OnPreviewKeyDown;
        if (_realtime is not null)
        {
            await _realtime.DisposeAsync().ConfigureAwait(true);
            _realtime = null;
        }

        if (_restBackup is not null)
        {
            await _restBackup.DisposeAsync().ConfigureAwait(true);
            _restBackup = null;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(true);
            _session = null;
        }

        try { _shotViewer?.Close(); } catch { /* ignore */ }
        _logWindow?.ForceClose();
    }

    private void ApplyTheme() => ThemeApplier.Apply(this, _settings, BuildPanelRefs());

    private TradingPanelRefs BuildPanelRefs() => new()
    {
        AccountPanel = AccountPanel,
        TickerPanel = TickerPanel,
        PositionsPanel = PositionsPanel,
        AccountTitle = AccountTitle,
        TickerTitle = TickerTitle,
        PositionsTitle = PositionsTitle,
        PendingTitle = PendingTitle,
        AccountText = AccountText,
        SymbolText = SymbolText,
        BidLabel = BidLabel,
        AskLabel = AskLabel,
        LotLabel = LotLabel,
        BidText = BidText,
        AskText = AskText,
        LotText = LotText,
        MarketText = MarketText,
        FlagsText = FlagsText,
        SourceText = SourceText,
        PositionsList = PositionsList,
        PendingList = PendingList,
        FeedBox = TerminalBox,
        StatusText = StatusText,
        HintText = HintText,
    };

    private async Task StartRealtimeAsync()
    {
        if (_realtime is not null)
        {
            await _realtime.DisposeAsync().ConfigureAwait(true);
            _realtime = null;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(true);
            _session = null;
        }

        _session = new SupabaseSessionClient(_settings);
        if (!_session.IsConfigured)
        {
            StatusText.Text = "Заполните Supabase URL и anon key в Настройках";
            ShowSettings();
            return;
        }

        await _session.EnsureSessionAsync().ConfigureAwait(true);

        _realtime = new SupabaseRealtimeMessages();
        _realtime.StatusChanged += OnRealtimeStatus;
        _realtime.LineReceived += OnLine;
        _realtime.Start(_settings.SupabaseUrl, _settings.SupabaseAnonKey);

        await BootstrapHwtFromSupabaseAsync().ConfigureAwait(true);
        SyncRestBackupBridge(forceRestart: false);
    }

    private async Task BootstrapHwtFromSupabaseAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "HWT: загрузка последнего снимка…";
            var lines = await _session.FetchLatestHwtSnapshotAsync().ConfigureAwait(true);
            if (lines.Count == 0)
            {
                StatusText.Text = "HWT: нет снимка — запрашиваю refresh…";
                AppendLocal("HWT bootstrap: пусто — INSERT refresh → Hermes.Mt5Terminal");
                AppLog.Info("HWT bootstrap: no hwt_status — requesting refresh");
                await RequestHwtRefreshAsync("start").ConfigureAwait(true);
                ScheduleHwtRefreshRetry();
                return;
            }

            foreach (var line in lines)
            {
                await HandleLineAsync(line, fromPoll: true).ConfigureAwait(true);
            }

            AppLog.Info($"HWT bootstrap: {lines.Count} row(s) from Supabase");
        }
        catch (Exception ex)
        {
            AppLog.Warn("HWT bootstrap: " + ex.Message);
            StatusText.Text = "HWT bootstrap error: " + Truncate(ex.Message, 80);
        }
    }

    private void ScheduleHwtRefreshRetry()
    {
        _hwtRefreshRetry?.Stop();
        _hwtRefreshRetry = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        _hwtRefreshRetry.Tick += async (_, __) =>
        {
            _hwtRefreshRetry?.Stop();
            if (_hwtStatusReceived)
            {
                return;
            }

            AppLog.Info("HWT bootstrap: retry refresh (no status yet)");
            await RequestHwtRefreshAsync("retry-20s").ConfigureAwait(true);
        };
        _hwtRefreshRetry.Start();
    }

    private async Task RequestHwtRefreshAsync(string reason)
    {
        if (_session is null)
        {
            return;
        }

        var asked = await _session.TryInsertCommandAsync("Hermes.Mt5Terminal", "refresh").ConfigureAwait(true);
        if (asked)
        {
            StatusText.Text = "HWT: refresh отправлен (" + reason + ") — нужен Hermes.Wpf + HWT";
            AppLog.Info("HWT refresh INSERT ok → Hermes.Mt5Terminal (" + reason + ")");
        }
        else
        {
            StatusText.Text = "HWT: refresh INSERT не удался (" + reason + ")";
            AppLog.Warn("HWT refresh INSERT failed (" + reason + ")");
        }
    }

    private void OnRealtimeStatus(string s)
    {
        Dispatcher.Invoke(() =>
        {
            if (_restBackup is { IsRunning: true })
            {
                StatusText.Text = s + " · REST backup active";
            }
            else
            {
                StatusText.Text = s;
            }
        });
        SyncRestBackupBridge(forceRestart: false);
    }

    private void StartBackupBridgeMonitor()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        timer.Tick += (_, __) => SyncRestBackupBridge(forceRestart: false);
        timer.Start();
    }

    private void SyncRestBackupBridge(bool forceRestart)
    {
        if (!_settings.RestBackupBridgeEnabled)
        {
            if (_restBackup is { IsRunning: true })
            {
                _restBackup.Stop();
                AppLog.Info("REST backup bridge stopped (disabled in settings)");
            }

            return;
        }

        var wsOnline = _realtime?.IsConnected == true;
        if (wsOnline)
        {
            _wsOfflineSince = DateTimeOffset.MinValue;
            if (_restBackup is { IsRunning: true })
            {
                _restBackup.Stop();
                AppLog.Info("REST backup bridge standby — WebSocket online");
                Dispatcher.Invoke(() => StatusText.Text = "WebSocket: активен");
            }

            return;
        }

        if (_wsOfflineSince == DateTimeOffset.MinValue)
        {
            _wsOfflineSince = DateTimeOffset.UtcNow;
        }

        if (DateTimeOffset.UtcNow - _wsOfflineSince < RestBackupGrace && !forceRestart)
        {
            return;
        }

        _restBackup ??= new SupabaseRestBackupBridge(_settings);
        if (forceRestart)
        {
            if (_restBackup.IsRunning)
            {
                _restBackup.Stop();
            }

            _restBackup = new SupabaseRestBackupBridge(_settings);
        }

        if (!_restBackup.IsRunning)
        {
            _restBackup.StatusChanged -= OnRestBackupStatus;
            _restBackup.LineReceived -= OnRestBackupLine;
            _restBackup.StatusChanged += OnRestBackupStatus;
            _restBackup.LineReceived += OnRestBackupLine;
            _restBackup.Start();
        }
    }

    private void OnRestBackupStatus(string s) =>
        Dispatcher.Invoke(() => StatusText.Text = s);

    private void OnRestBackupLine(TerminalLine line) => _ = HandleLineAsync(line, fromPoll: true, fromRestBackup: true);

    private void OnLine(TerminalLine line) => _ = HandleLineAsync(line, fromPoll: false, fromRestBackup: false);

    private async Task HandleLineAsync(TerminalLine line, bool fromPoll, bool fromRestBackup = false)
    {
        if (!TryAcceptFeedLine(line))
        {
            return;
        }

        if (!_settings.ShowAllRecipients
            && !string.Equals(line.Recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if ((line.Content ?? string.Empty).StartsWith("[LOG:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (await TryHandleScreenshotAsync(line).ConfigureAwait(true))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (HwtStatusView.TryParse(line.Content, out var hwt))
            {
                _hwtStatusReceived = true;
                hwt.Source = fromRestBackup ? "rest-backup" : fromPoll ? "poll" : "supabase";
                ApplyHwt(hwt);
                AppLog.Info($"HWT status [{hwt.Source}] {hwt.Symbol} bid={hwt.Bid} ask={hwt.Ask}");
                AppendLocal($"{FormatTime(line.CreatedAt)}  HWT update ({line.Sender})");
                return;
            }

            var tag = fromRestBackup ? " [REST]" : fromPoll ? " [poll]" : string.Empty;
            AppendLocal($"{FormatTime(line.CreatedAt)}  {line.Sender} → {line.Recipient}{tag}  {Flatten(line.Content)}");
        });
    }

    private bool TryAcceptFeedLine(TerminalLine line)
    {
        lock (_feedDedupGate)
        {
            if (line.MessageId is { } id && id != Guid.Empty)
            {
                return _seenMessageIds.Add(id);
            }

            var key = (line.Sender ?? "") + "|" + (line.CreatedAt ?? "") + "|" + (line.Content ?? "");
            if (_seenFeedKeys.Count > 5000)
            {
                _seenFeedKeys.Clear();
                _seenMessageIds.Clear();
            }

            return _seenFeedKeys.Add(key);
        }
    }

    private async Task<bool> TryHandleScreenshotAsync(TerminalLine line)
    {
        if (HwtScreenshotMessage.TryParseRepeat(line.Content))
        {
            Dispatcher.Invoke(() =>
            {
                if (_shotViewer is { IsLoaded: true } && _shotViewer.ShowAgain(s => StatusText.Text = s, "повтор"))
                {
                    AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot REPEAT (кэш · 10 с)");
                    AppLog.Info("Screenshot: repeat → 10s show from cache");
                }
                else
                {
                    StatusText.Text = "Screenshot repeat: нет кэша — нужен новый Screenshot";
                    AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot REPEAT FAIL (нет кэша)");
                }
            });
            return true;
        }

        if (!HwtScreenshotMessage.TryParse(line.Content, out var shot))
        {
            return false;
        }

        // Unique per publish (nonce) so «повтор» / re-upload always restarts the 10s viewer.
        var key = shot.DedupKey;
        var isReplay = shot.Replay;
        if (!isReplay && !_seenShotKeys.Add(key))
        {
            return true;
        }

        if (isReplay)
        {
            _seenShotKeys.Add(key);
        }

        byte[]? bytes = null;
        if (!string.IsNullOrWhiteSpace(shot.DataBase64))
        {
            try
            {
                bytes = Convert.FromBase64String(shot.DataBase64);
            }
            catch (Exception ex)
            {
                AppLog.Warn("screenshot base64: " + ex.Message);
            }
        }
        else if (_session is not null
                 && !string.IsNullOrWhiteSpace(shot.Bucket)
                 && !string.IsNullOrWhiteSpace(shot.Path))
        {
            bytes = await _session.DownloadStorageObjectAsync(shot.Bucket!, shot.Path!).ConfigureAwait(true);
        }

        if (bytes is null || bytes.Length == 0)
        {
            // Replay without downloadable payload — show cached image for 10s.
            if (isReplay)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_shotViewer is { IsLoaded: true } && _shotViewer.ShowAgain(s => StatusText.Text = s, "повтор-fallback"))
                    {
                        AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot REPEAT (кэш)");
                    }
                    else
                    {
                        StatusText.Text = "Screenshot: не удалось скачать / нет кэша";
                        AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot FAIL ({shot.Name})");
                    }
                });
                return true;
            }

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Screenshot: не удалось скачать файл";
                AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot FAIL ({shot.Name})");
            });
            return true;
        }

        Dispatcher.Invoke(() =>
        {
            AppendLocal($"{FormatTime(line.CreatedAt)}  screenshot ← {line.Sender} · {shot.Name}"
                        + (isReplay ? " (повтор · 10 с)" : ""));
            OpenScreenshotViewer(bytes, shot.Name);
        });
        return true;
    }

    private void OpenScreenshotViewer(byte[] pngBytes, string label)
    {
        if (_shotViewer == null || !_shotViewer.IsLoaded)
        {
            _shotViewer = new ScreenshotViewerWindow();
            _shotViewer.Closed += (_, __) => _shotViewer = null;
        }

        _shotViewer.ShowScreenshot(pngBytes, label, s =>
        {
            void Apply()
            {
                StatusText.Text = s;
                if (s.Contains("Пробел", StringComparison.Ordinal)
                    || s.Contains("пробуждение", StringComparison.Ordinal)
                    || s.Contains("мониторы выкл", StringComparison.Ordinal)
                    || s.Contains("показ", StringComparison.Ordinal))
                {
                    AppLog.Info(s);
                }
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        });
        _shotViewer.Activate();
        _shotViewer.Topmost = true;
    }

    private void ApplyHwt(HwtStatusView view)
    {
        AccountText.Text = string.IsNullOrWhiteSpace(view.Account)
            ? "—"
            : view.Account.Replace(" | ", Environment.NewLine);

        SymbolText.Text = string.IsNullOrWhiteSpace(view.Symbol) ? "—" : view.Symbol.Trim();
        BidText.Text = string.IsNullOrWhiteSpace(view.Bid) ? "—" : view.Bid.Trim();
        AskText.Text = string.IsNullOrWhiteSpace(view.Ask) ? "—" : view.Ask.Trim();
        LotText.Text = string.IsNullOrWhiteSpace(view.Lot) ? "—" : view.Lot.Trim();
        MarketText.Text = string.IsNullOrWhiteSpace(view.MarketStatus) ? "" : view.MarketStatus.Trim();
        FlagsText.Text = $"Real trading: {(view.RealTrading ? "ON" : "OFF")}   Auto: {(view.AutoTrade ? "ON" : "OFF")}";
        SourceText.Text = string.IsNullOrWhiteSpace(view.Source) ? "" : $"[{view.Source}]";

        var open = view.OpenPositions;
        PositionsList.Text = open.Count == 0
            ? (string.IsNullOrWhiteSpace(view.PositionsHeader) ? "(нет открытых позиций)" : view.PositionsHeader + "\n(нет строк)")
            : string.Join(Environment.NewLine, open);

        var pending = view.ResolvedPending;
        PendingList.Text = pending.Count == 0
            ? "(нет / HWT пока не отдаёт pending отдельно)"
            : string.Join(Environment.NewLine, pending);
    }

    private void AppendLocal(string text)
    {
        if (_lineCount >= _settings.MaxLines)
        {
            TerminalBox.Clear();
            _lineCount = 0;
            TerminalBox.AppendText("--- truncated ---" + Environment.NewLine);
        }

        TerminalBox.AppendText(text + Environment.NewLine);
        TerminalBox.CaretIndex = TerminalBox.Text.Length;
        TerminalBox.ScrollToEnd();
        _lineCount++;
    }

    private static string FormatTime(string createdAt) =>
        DateTimeOffset.TryParse(createdAt, out var dto)
            ? dto.ToLocalTime().ToString("HH:mm:ss")
            : DateTime.Now.ToString("HH:mm:ss");

    private static string Flatten(string content) =>
        string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Replace("\r\n", " ¶ ").Replace('\n', '¶').Replace('\r', '¶');

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        TerminalBox.Clear();
        _lineCount = 0;
    }

    private void Log_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logWindow is null || !_logWindow.IsLoaded)
        {
            _logWindow = new LogWindow { Owner = this };
            _logWindow.SendToSupabaseRequested += OnSendLogsToSupabaseAsync;
            _logWindow.Show();
            return;
        }

        _logWindow.Activate();
    }

    private async Task OnSendLogsToSupabaseAsync(string payload)
    {
        if (_session is null)
        {
            MessageBox.Show(this, "Нет клиента Supabase. Проверьте URL/key и Reconnect.", "Лог → Supabase",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _session.EnsureSessionAsync().ConfigureAwait(true);
        if (!_session.HasUserSession)
        {
            MessageBox.Show(this, "Нет сессии Supabase. Проверьте URL/key и Reconnect.", "Лог → Supabase",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ok = await _session.TryInsertLogAsync(payload).ConfigureAwait(true);
        MessageBox.Show(this,
            ok ? "Логи отправлены → " + _settings.RecipientName : "INSERT не удался.",
            "Лог → Supabase", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Settings_OnClick(object sender, RoutedEventArgs e) => ShowSettings();

    private async void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        ApplyTheme();
        AppendLocal("Reconnect WebSocket…");
        await StartRealtimeAsync().ConfigureAwait(true);
    }

    private async void Poll_OnClick(object sender, RoutedEventArgs e) => await RunPollAsync().ConfigureAwait(true);

    private async void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space
            && _shotViewer is { IsLoaded: true, IsAsleep: true }
            && _shotViewer.TryWakeWithSpace())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await RunPollAsync().ConfigureAwait(true);
        }
    }

    private async Task RunPollAsync()
    {
        if (_pollBusy || _session is null)
        {
            return;
        }

        _pollBusy = true;
        StatusText.Text = "Poll…";
        try
        {
            var lines = await _session.PollRecentMessagesAsync().ConfigureAwait(true);
            AppendLocal($"Poll: {lines.Count} new");
            foreach (var line in lines)
            {
                await HandleLineAsync(line, fromPoll: true).ConfigureAwait(true);
            }

            if (lines.Count == 0)
            {
                StatusText.Text = "Poll: нет новых сообщений";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Poll error: " + Truncate(ex.Message, 80);
            AppLog.Warn("Poll: " + ex.Message);
        }
        finally
        {
            _pollBusy = false;
        }
    }

    private async void ShowSettings()
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        SettingsStore.Save(_settings);
        MirrorLogsCheck.IsChecked = _settings.MirrorLocalLogsToSupabase;
        ApplyTheme();
        AppendLocal($"Settings saved → {SettingsStore.SettingsPath}");
        await StartRealtimeAsync().ConfigureAwait(true);
    }

    private void Mirror_OnChanged(object sender, RoutedEventArgs e)
    {
        _settings.MirrorLocalLogsToSupabase = MirrorLogsCheck.IsChecked == true;
        SettingsStore.Save(_settings);
    }

    private void OnAppLogLine(string line)
    {
        if (!_settings.MirrorLocalLogsToSupabase || _mirroringLog || _session is null)
        {
            return;
        }

        if (line.Contains("[LOG:RemoteTerminal]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("WebSocket:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("HWT bootstrap", StringComparison.OrdinalIgnoreCase)
            || line.Contains("REST backup", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Supabase session OK", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Realtime WebSocket connected", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = MirrorAsync(line);
    }

    private async Task MirrorAsync(string line)
    {
        if (_session is null)
        {
            return;
        }

        _mirroringLog = true;
        try
        {
            await _session.TryInsertLogAsync("[LOG:RemoteTerminal] " + line).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _mirroringLog = false;
        }
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s[..n] + "…");
}

/// <summary>Parses <c>hwt_screenshot</c> / <c>hwt_screenshot_repeat</c> / image <c>file</c> JSON.</summary>
internal static class HwtScreenshotMessage
{
    public static bool TryParseRepeat(string? content)
    {
        var t = (content ?? string.Empty).Trim();
        if (t.Length < 10 || t[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var type = doc.RootElement.TryGetProperty("type", out var ty) ? ty.GetString() : null;
            return string.Equals(type, "hwt_screenshot_repeat", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParse(string? content, out Parsed shot)
    {
        shot = default;
        var t = (content ?? string.Empty).Trim();
        if (t.Length < 10 || t[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var ty) ? ty.GetString() : null;
            if (string.Equals(type, "hwt_screenshot", StringComparison.OrdinalIgnoreCase))
            {
                var replay = root.TryGetProperty("replay", out var rp)
                             && rp.ValueKind == JsonValueKind.True;
                var nonce = root.TryGetProperty("nonce", out var nn) ? nn.GetString() : null;
                shot = new Parsed(
                    root.TryGetProperty("name", out var n) ? n.GetString() ?? "shot.png" : "shot.png",
                    root.TryGetProperty("bucket", out var b) ? b.GetString() : null,
                    root.TryGetProperty("path", out var p) ? p.GetString() : null,
                    root.TryGetProperty("data_base64", out var d) ? d.GetString() : null,
                    replay,
                    nonce);
                return !string.IsNullOrWhiteSpace(shot.Path) || !string.IsNullOrWhiteSpace(shot.DataBase64);
            }

            if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                var mime = root.TryGetProperty("mime", out var m) ? m.GetString() ?? "" : "";
                if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                shot = new Parsed(
                    root.TryGetProperty("name", out var n) ? n.GetString() ?? "file.png" : "file.png",
                    root.TryGetProperty("bucket", out var b) ? b.GetString() : "chat-files",
                    root.TryGetProperty("path", out var p) ? p.GetString() : null,
                    null,
                    replay: false,
                    nonce: null);
                return !string.IsNullOrWhiteSpace(shot.Path);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal readonly struct Parsed
    {
        public Parsed(string name, string? bucket, string? path, string? dataBase64, bool replay, string? nonce)
        {
            Name = name;
            Bucket = bucket;
            Path = path;
            DataBase64 = dataBase64;
            Replay = replay;
            Nonce = nonce;
        }

        public string Name { get; }
        public string? Bucket { get; }
        public string? Path { get; }
        public string? DataBase64 { get; }
        public bool Replay { get; }
        public string? Nonce { get; }

        public string DedupKey =>
            !string.IsNullOrWhiteSpace(Nonce) ? "n:" + Nonce!
            : !string.IsNullOrWhiteSpace(Path) ? Path!
            : !string.IsNullOrWhiteSpace(DataBase64) ? "b64:" + DataBase64!.Length + ":" + Name
            : Name;
    }
}
