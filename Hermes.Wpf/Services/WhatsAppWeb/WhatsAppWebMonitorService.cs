using System.Security.Cryptography;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services.WhatsAppWeb;

public sealed class WhatsAppWebMonitorService : IAsyncDisposable
{
    private const int DefaultMinForwardTextLength = 2;
    private const int BaselineStablePollsRequired = 3;
    private const int BaselineMaxAttempts = 12;
    private const int ParseProbeInitialMaxAttempts = 1;
    private const int ParseProbeAutoRetryMaxAttempts = 1;
    private const int ParseProbeMaxAutoRetryCycles = 0;
    internal const string ParseProbePrefix = "[hermes-probe:";
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ParseProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ParseProbeAutoRetryCooldown = TimeSpan.FromMinutes(10);

    private readonly WhatsAppWebLogService _log;
    private readonly WhatsAppWebReader _reader;
    private readonly object _sync = new();
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private bool _baselineDone;
    private bool _parseProbeConfirmed;
    private int _pollTicks;
    private int _lastLoggedPollStatusTick;
    private string _lastPollStatus = string.Empty;
    private DateTimeOffset? _lastOkPollUtc;
    private WhatsAppMonitorReadiness _readiness = WhatsAppMonitorReadiness.Off;
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private WhatsAppSeenMessageStore? _seenStore;
    private string _contactName = "My Fido";
    private string _textMarker = string.Empty;
    private int _pollIntervalMs = 2000;
    private int _minForwardTextLength = DefaultMinForwardTextLength;
    private readonly object _probeSync = new();
    private TaskCompletionSource<WhatsAppParseProbeResult>? _activeProbe;
    private string _activeProbeText = string.Empty;
    private DateTimeOffset _probeStartedUtc;
    private bool _parseProbeEnabled = true;
    private DateTimeOffset? _lastProbeCycleUtc;
    private int _probeAutoRetryCycles;
    private bool _pollingDespiteUnconfirmedProbe;
    private int _lastDomMessageCount;

    public WhatsAppWebMonitorService(WhatsAppWebLogService log, WhatsAppWebReader reader)
    {
        _log = log;
        _reader = reader;
    }

    public event Action<WhatsAppMessage>? MessageReceived;

    public event Action<string>? StatusChanged;

    public event Action<WhatsAppMonitorReadiness, string>? ReadinessChanged;

    public WhatsAppMonitorReadiness CurrentReadiness => _readiness;

    public void ShowWhatsAppWindow() => _reader.ShowWindow();

    public Task<WhatsAppParseProbeResult> RunParseProbeAsync(CancellationToken cancellationToken = default) =>
        RunParseProbeAndConfirmAsync(cancellationToken);

    private async Task<WhatsAppParseProbeResult> RunParseProbeAndConfirmAsync(CancellationToken cancellationToken)
    {
        _probeAutoRetryCycles = 0;
        var result = await RunParseProbeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _parseProbeConfirmed = true;
            NoteSuccessfulPoll();
            _log.LogInfo($"[whatsapp] Probe detected in DOM ({result.DetectLatencyMs} ms)");
        }

        return result;
    }

    public void ApplyForwardingOptions(int minForwardTextLength, string? textMarker)
    {
        lock (_sync)
        {
            _minForwardTextLength = minForwardTextLength < 1 ? 1 : minForwardTextLength;
            _textMarker = textMarker?.Trim() ?? string.Empty;
        }

        var filterHint = string.IsNullOrWhiteSpace(_textMarker) ? "(all messages)" : _textMarker;
        _log.LogInfo(
            $"[whatsapp] Forwarding options updated: minText={_minForwardTextLength}, filter «{filterHint}»");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RaiseReadiness(WhatsAppMonitorReadiness.Starting, "Загрузка web.whatsapp.com…");
        await _reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _log.LogInfo("[whatsapp] WebView2 initialized → web.whatsapp.com");
        _ = Task.Run(() => WaitAndOpenContactAsync(cancellationToken), cancellationToken);
    }

    public Task StartMonitoringAsync(
        string contactDisplayName,
        int pollIntervalMs,
        string? textMarker,
        bool parseProbeEnabled = true,
        int minForwardTextLength = DefaultMinForwardTextLength,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            StopMonitoringCore();
            _contactName = string.IsNullOrWhiteSpace(contactDisplayName) ? "My Fido" : contactDisplayName.Trim();
            _textMarker = textMarker?.Trim() ?? string.Empty;
            _pollIntervalMs = pollIntervalMs < 500 ? 500 : pollIntervalMs;
            _minForwardTextLength = minForwardTextLength < 1 ? 1 : minForwardTextLength;
            _parseProbeEnabled = parseProbeEnabled;
            _baselineDone = false;
            _parseProbeConfirmed = false;
            _pollingDespiteUnconfirmedProbe = false;
            _lastProbeCycleUtc = null;
            _probeAutoRetryCycles = 0;
            _pollTicks = 0;
            _lastLoggedPollStatusTick = 0;
            _lastPollStatus = string.Empty;
            _lastOkPollUtc = null;
            _seenIds.Clear();
            _seenStore = new WhatsAppSeenMessageStore(_contactName);
            foreach (var id in _seenStore.AllIds)
            {
                _seenIds.Add(id);
            }

            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitorTask = MonitorLoopAsync(_monitorCts.Token);
        }

        var filterHint = string.IsNullOrWhiteSpace(_textMarker) ? "(all messages)" : _textMarker;
        _log.LogInfo(
            $"[whatsapp] Monitoring «{_contactName}», filter «{filterHint}», poll {_pollIntervalMs} ms, minText={_minForwardTextLength}, persisted seen={_seenIds.Count}");
        RaiseReadiness(WhatsAppMonitorReadiness.Starting, $"Подключение к чату «{_contactName}»…");
        return Task.CompletedTask;
    }

    public void StopMonitoring()
    {
        lock (_sync)
        {
            StopMonitoringCore();
        }
    }

    private void StopMonitoringCore()
    {
        if (_monitorCts is null)
        {
            return;
        }

        _monitorCts.Cancel();
        _monitorCts.Dispose();
        _monitorCts = null;
        _monitorTask = null;
        RaiseReadiness(WhatsAppMonitorReadiness.Off, string.Empty);
    }

    private void RaiseReadiness(WhatsAppMonitorReadiness state, string message)
    {
        if (_readiness == state && string.Equals(_lastReadinessMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _readiness = state;
        _lastReadinessMessage = message;
        ReadinessChanged?.Invoke(state, message);
    }

    private string _lastReadinessMessage = string.Empty;

    private void NoteSuccessfulPoll()
    {
        _lastOkPollUtc = DateTimeOffset.UtcNow;

        if (_baselineDone && _readiness is WhatsAppMonitorReadiness.Stalled or WhatsAppMonitorReadiness.OpeningChat)
        {
            if (_parseProbeConfirmed)
            {
                RaiseReadiness(
                    WhatsAppMonitorReadiness.Ready,
                    $"Готов — новые сообщения в «{_contactName}» будут добавлены в чат");
            }
        }
    }

    private void MaybeRaiseStalled(string pollStatus)
    {
        if (!_baselineDone || _readiness == WhatsAppMonitorReadiness.QrRequired)
        {
            return;
        }

        if (_lastOkPollUtc is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - _lastOkPollUtc.Value < StallThreshold)
        {
            return;
        }

        if (_readiness == WhatsAppMonitorReadiness.Stalled)
        {
            return;
        }

        _log.LogWarn($"[whatsapp] Poll stalled ({pollStatus}) — no successful poll for {StallThreshold.TotalSeconds:0}s");
        RaiseReadiness(
            WhatsAppMonitorReadiness.Stalled,
            "WhatsApp Web не отвечает — откройте окно WhatsApp или перезапустите в Settings");
    }

    private async Task WaitAndOpenContactAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 45 && !cancellationToken.IsCancellationRequested; i++)
        {
            var ready = await _reader.ExecuteJsonAsync<WhatsAppWebSimpleResult>(
                WhatsAppWebScriptBuilder.BuildIsReadyScript(),
                cancellationToken).ConfigureAwait(false);

            if (ready?.Status == "qr")
            {
                RaiseReadiness(WhatsAppMonitorReadiness.QrRequired, "Сканируйте QR в окне WhatsApp Web");
                RaiseStatus("Сканируйте QR в окне WhatsApp Web");
                _reader.ShowWindow();
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (ready?.Status == "ready")
            {
                _log.LogInfo($"[whatsapp] WhatsApp Web ready, chats={ready.ChatCount}");
                await TryOpenContactAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryOpenContactAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            var result = await _reader.ExecuteJsonAsync<WhatsAppWebSimpleResult>(
                WhatsAppWebScriptBuilder.BuildOpenChatScript(_contactName),
                cancellationToken).ConfigureAwait(false);

            if (result?.Status is "opened" or "already_open")
            {
                _log.LogInfo($"[whatsapp] Chat opened: «{_contactName}» ({result.Status})");
                RaiseReadiness(WhatsAppMonitorReadiness.OpeningChat, $"Чат «{_contactName}» открыт — подготовка…");
                var filterHint = string.IsNullOrWhiteSpace(_textMarker) ? "новые" : _textMarker;
                RaiseStatus($"Чат «{_contactName}» открыт — ждём {filterHint}…");
                return true;
            }

            if (result?.Status == "qr")
            {
                RaiseReadiness(WhatsAppMonitorReadiness.QrRequired, "Требуется QR-авторизация WhatsApp Web");
                _reader.ShowWindow();
                RaiseStatus("Требуется QR-авторизация WhatsApp Web");
                return false;
            }

            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        }

        _log.LogWarn($"[whatsapp] Chat «{_contactName}» not found after retries");
        RaiseReadiness(WhatsAppMonitorReadiness.Error, $"Чат «{_contactName}» не найден в WhatsApp Web");
        RaiseStatus($"Чат «{_contactName}» не найден");
        _reader.ShowWindow();
        return false;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        if (!await TryOpenContactAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await Task.Delay(800, cancellationToken).ConfigureAwait(false);
        await EstablishBaselineAfterChatOpenAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[whatsapp] Poll error: {ex.Message}");
            }

            await Task.Delay(_pollIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EstablishBaselineAfterChatOpenAsync(CancellationToken cancellationToken)
    {
        RaiseReadiness(
            WhatsAppMonitorReadiness.Baseline,
            "Базовая линия WhatsApp — история не импортируется, только новые сообщения");

        await _reader.ExecuteJsonAsync<WhatsAppWebSimpleResult>(
            WhatsAppWebScriptBuilder.BuildScrollToBottomScript(),
            cancellationToken).ConfigureAwait(false);

        var lastCount = -1;
        var stablePolls = 0;
        var totalMarked = 0;

        for (var attempt = 0; attempt < BaselineMaxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            await _reader.ExecuteJsonAsync<WhatsAppWebSimpleResult>(
                WhatsAppWebScriptBuilder.BuildScrollToBottomScript(),
                cancellationToken).ConfigureAwait(false);

            var result = await _reader.ExecuteJsonAsync<WhatsAppWebPollResult>(
                WhatsAppWebScriptBuilder.BuildPollScript(_contactName, _textMarker, forBaseline: true),
                cancellationToken).ConfigureAwait(false);

            if (result?.Status is "opening" or "loading")
            {
                _log.LogInfo($"[whatsapp] Baseline wait: status={result.Status}, attempt={attempt + 1}");
                await Task.Delay(600, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (result?.Status != "ok")
            {
                _log.LogWarn($"[whatsapp] Baseline aborted: status={result?.Status ?? "null"}, attempt={attempt + 1}");
                break;
            }

            var markedThisPass = 0;
            foreach (var m in result.Messages)
            {
                var text = (m.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var unstableId = IsUnstableRawId(m.Id);
                var id = NormalizeId(m);
                if (!IsMessageSeen(id, text, unstableId))
                {
                    MarkMessageSeen(id, text, unstableId);
                    markedThisPass++;
                }
            }

            totalMarked += markedThisPass;
            var count = result.Messages.Count(m => m.HasMessageStructure);
            if (count == lastCount)
            {
                stablePolls++;
            }
            else
            {
                stablePolls = 0;
                lastCount = count;
            }

            _log.LogInfo(
                $"[whatsapp] Baseline pass {attempt + 1}: dom={count}, newlyMarked={markedThisPass}, stable={stablePolls}/{BaselineStablePollsRequired}");

            if (stablePolls >= BaselineStablePollsRequired)
            {
                _baselineDone = true;
                await (_seenStore?.FlushAsync() ?? Task.CompletedTask).ConfigureAwait(false);
                _log.LogInfo(
                    $"[whatsapp] Baseline complete: {count} message(s) in DOM, {_seenIds.Count} total seen ids (not forwarded to Hermes chat)");

                if (_parseProbeEnabled)
                {
                    await ConfirmParseReadyWithProbeAsync(
                        cancellationToken,
                        ParseProbeInitialMaxAttempts,
                        isAutoRetry: false).ConfigureAwait(false);
                }
                else
                {
                    _parseProbeConfirmed = true;
                    NoteSuccessfulPoll();
                    RaiseReadiness(
                        WhatsAppMonitorReadiness.Ready,
                        $"Готов — новые сообщения в «{_contactName}» будут добавлены в чат");
                }

                RaiseStatus($"WhatsApp baseline: {_seenIds.Count} id — только новые сообщения");
                return;
            }

            await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        }

        if (!_baselineDone && totalMarked > 0)
        {
            _baselineDone = true;
            await (_seenStore?.FlushAsync() ?? Task.CompletedTask).ConfigureAwait(false);
            _log.LogWarn(
                $"[whatsapp] Baseline forced after max attempts: {_seenIds.Count} seen ids (DOM may still be loading)");

            if (_parseProbeEnabled)
            {
                await ConfirmParseReadyWithProbeAsync(
                    cancellationToken,
                    ParseProbeInitialMaxAttempts,
                    isAutoRetry: false).ConfigureAwait(false);
            }
            else
            {
                _parseProbeConfirmed = true;
                NoteSuccessfulPoll();
                RaiseReadiness(
                    WhatsAppMonitorReadiness.Ready,
                    $"Готов (baseline частичный) — «{_contactName}»");
            }
        }
        else if (!_baselineDone)
        {
            _log.LogWarn("[whatsapp] Baseline not captured — polling will retry baseline before accepting messages");
            RaiseReadiness(WhatsAppMonitorReadiness.OpeningChat, "Ожидание загрузки чата WhatsApp…");
        }
    }

    private async Task ConfirmParseReadyWithProbeAsync(
        CancellationToken cancellationToken,
        int maxAttempts,
        bool isAutoRetry)
    {
        _lastProbeCycleUtc = DateTimeOffset.UtcNow;
        if (isAutoRetry)
        {
            _probeAutoRetryCycles++;
        }

        for (var attempt = 0; attempt < maxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            var result = await RunParseProbeCoreAsync(cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _parseProbeConfirmed = true;
                _pollingDespiteUnconfirmedProbe = false;
                NoteSuccessfulPoll();
                RaiseReadiness(
                    WhatsAppMonitorReadiness.Ready,
                    $"Готов — парсинг проверен ({result.DetectLatencyMs} ms)");
                RaiseStatus($"WhatsApp probe OK ({result.DetectLatencyMs} ms)");
                return;
            }

            _log.LogWarn(
                $"[whatsapp] Parse probe failed: {result.FailureReason}, attempt {attempt + 1}/{maxAttempts}" +
                (isAutoRetry ? $", auto-retry cycle {_probeAutoRetryCycles}/{ParseProbeMaxAutoRetryCycles}" : string.Empty));

            if (attempt + 1 < maxAttempts)
            {
                await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
            }
        }

        _pollingDespiteUnconfirmedProbe = true;

        if (_probeAutoRetryCycles >= ParseProbeMaxAutoRetryCycles)
        {
            RaiseReadiness(
                WhatsAppMonitorReadiness.Ready,
                $"Готов — мониторинг «{_contactName}» (тест отправки не прошёл, «Тест» для повтора)");
            RaiseStatus("WhatsApp: мониторинг активен, probe отправки остановлен");
            return;
        }

        var retryHint = $"авто-тест через {ParseProbeAutoRetryCooldown.TotalMinutes:0} мин или кнопка «Тест»";

        RaiseReadiness(
            WhatsAppMonitorReadiness.Ready,
            $"Готов — мониторинг «{_contactName}» ({retryHint})");
        RaiseStatus("WhatsApp: мониторинг активен, тест отправки не прошёл");
    }

    private bool ShouldRunAutoProbeRetry()
    {
        if (_parseProbeConfirmed || !_parseProbeEnabled || !_pollingDespiteUnconfirmedProbe)
        {
            return false;
        }

        if (_probeAutoRetryCycles >= ParseProbeMaxAutoRetryCycles)
        {
            return false;
        }

        if (_lastProbeCycleUtc is null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - _lastProbeCycleUtc.Value >= ParseProbeAutoRetryCooldown;
    }

    private async Task<WhatsAppParseProbeResult> RunParseProbeCoreAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<WhatsAppParseProbeResult>? probeTcs;
        lock (_probeSync)
        {
            if (_activeProbe is not null)
            {
                return new WhatsAppParseProbeResult
                {
                    Success = false,
                    FailureReason = "probe_already_running",
                };
            }

            var token = Guid.NewGuid().ToString("N")[..8];
            _activeProbeText = $"{ParseProbePrefix}{token}]";
            _probeStartedUtc = DateTimeOffset.UtcNow;
            probeTcs = new TaskCompletionSource<WhatsAppParseProbeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeProbe = probeTcs;
        }

        RaiseReadiness(
            WhatsAppMonitorReadiness.Probing,
            "Проверка парсинга — отправка тестового сообщения в WhatsApp…");

        var startedUtc = DateTimeOffset.UtcNow;
        try
        {
            var sendResult = await SendProbeMessageAsync(_activeProbeText, cancellationToken).ConfigureAwait(false);

            var sendStatus = sendResult?.Status ?? "null";
            if (sendStatus is not "sent")
            {
                var detail = sendResult?.Detail ?? sendResult?.Remaining ?? string.Empty;
                var raw = sendResult?.RawJson ?? string.Empty;
                _log.LogWarn(
                    $"[whatsapp] Probe send failed: status={sendStatus}, method={sendResult?.Method}, remaining=«{sendResult?.Remaining}», detail={detail}, raw={raw}");
                return CompleteActiveProbe(new WhatsAppParseProbeResult
                {
                    Success = false,
                    ProbeText = _activeProbeText,
                    FailureReason = $"send:{sendStatus}:{detail}",
                });
            }

            _log.LogInfo(
                $"[whatsapp] Probe sent «{_activeProbeText}» via {sendResult?.Method}, attempt={sendResult?.Attempt}, waiting for DOM…");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ParseProbeTimeout);

            var detectTask = probeTcs.Task;
            while (!cancellationToken.IsCancellationRequested)
            {
                WhatsAppParseProbeResult completed;
                if (detectTask.IsCompleted)
                {
                    completed = await detectTask.ConfigureAwait(false);
                    return completed;
                }

                await PollOnceForProbeAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_pollIntervalMs, cancellationToken).ConfigureAwait(false);

                if (DateTimeOffset.UtcNow - startedUtc > ParseProbeTimeout)
                {
                    break;
                }
            }

            return CompleteActiveProbe(new WhatsAppParseProbeResult
            {
                Success = false,
                ProbeText = _activeProbeText,
                FailureReason = "detect_timeout",
                DetectLatencyMs = (int)(DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CompleteActiveProbe(new WhatsAppParseProbeResult
            {
                Success = false,
                ProbeText = _activeProbeText,
                FailureReason = "cancelled",
            });
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[whatsapp] Probe error: {ex.Message}");
            return CompleteActiveProbe(new WhatsAppParseProbeResult
            {
                Success = false,
                ProbeText = _activeProbeText,
                FailureReason = ex.Message,
            });
        }
    }

    private async Task<WhatsAppWebSendAttemptResult?> SendProbeMessageAsync(
        string messageText,
        CancellationToken cancellationToken)
    {
        var send = await _reader.SendTextViaCdpAsync(messageText, cancellationToken).ConfigureAwait(false);
        return new WhatsAppWebSendAttemptResult
        {
            Status = send?.Status ?? "null",
            Method = send?.Method ?? "cdp",
            Remaining = send?.Remaining ?? string.Empty,
            Detail = send?.Detail ?? string.Empty,
            RawJson = send is null ? "null" : System.Text.Json.JsonSerializer.Serialize(send),
        };
    }

    private sealed class WhatsAppWebSendAttemptResult
    {
        public string Status { get; init; } = string.Empty;

        public string Method { get; init; } = string.Empty;

        public int Attempt { get; init; }

        public string Remaining { get; init; } = string.Empty;

        public string Detail { get; init; } = string.Empty;

        public string RawJson { get; init; } = string.Empty;
    }

    private async Task PollOnceForProbeAsync(CancellationToken cancellationToken)
    {
        var result = await _reader.ExecuteJsonAsync<WhatsAppWebPollResult>(
            WhatsAppWebScriptBuilder.BuildPollScript(_contactName, _textMarker, forBaseline: true),
            cancellationToken).ConfigureAwait(false);

        if (result?.Status == "ok")
        {
            ProcessNewMessages(result.Messages, fromProbePoll: true);
        }
    }

    private WhatsAppParseProbeResult CompleteActiveProbe(WhatsAppParseProbeResult result)
    {
        lock (_probeSync)
        {
            _activeProbe?.TrySetResult(result);
            _activeProbe = null;
            _activeProbeText = string.Empty;
        }

        return result;
    }

    private void TryCompleteProbeFromMessage(string text)
    {
        TaskCompletionSource<WhatsAppParseProbeResult>? probeTcs;
        lock (_probeSync)
        {
            probeTcs = _activeProbe;
            if (probeTcs is null || string.IsNullOrEmpty(_activeProbeText))
            {
                return;
            }

            if (!text.Contains(_activeProbeText, StringComparison.Ordinal))
            {
                return;
            }

            _activeProbe = null;
            _activeProbeText = string.Empty;
        }

        probeTcs.TrySetResult(new WhatsAppParseProbeResult
        {
            Success = true,
            ProbeText = text,
            DetectLatencyMs = (int)(DateTimeOffset.UtcNow - _probeStartedUtc).TotalMilliseconds,
        });
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        _pollTicks++;

        if (!_baselineDone)
        {
            await EstablishBaselineAfterChatOpenAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (ShouldRunAutoProbeRetry())
        {
            _log.LogInfo(
                $"[whatsapp] Auto-retry parse probe (cycle {_probeAutoRetryCycles + 1}/{ParseProbeMaxAutoRetryCycles}, cooldown {ParseProbeAutoRetryCooldown.TotalMinutes:0} min)");
            await ConfirmParseReadyWithProbeAsync(
                cancellationToken,
                ParseProbeAutoRetryMaxAttempts,
                isAutoRetry: true).ConfigureAwait(false);
        }

        var result = await PollChatReliableAsync(cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            MaybeLogPollHeartbeat("null_result");
            MaybeRaiseStalled("null_result");
            return;
        }

        switch (result.Status)
        {
            case "qr":
                RaiseReadiness(WhatsAppMonitorReadiness.QrRequired, "Сканируйте QR в окне WhatsApp Web");
                RaiseStatus("WhatsApp Web: нужен QR");
                _reader.ShowWindow();
                MaybeLogPollHeartbeat("qr");
                return;
            case "loading":
            case "opening":
                MaybeLogPollHeartbeat(result.Status);
                MaybeRaiseStalled(result.Status);
                return;
            case "chat_not_found":
                RaiseReadiness(WhatsAppMonitorReadiness.Error, $"Чат «{_contactName}» не найден");
                RaiseStatus($"Чат «{_contactName}» не найден");
                MaybeLogPollHeartbeat("chat_not_found");
                return;
            case "ok":
                NoteSuccessfulPoll();
                var newCount = ProcessNewMessages(result.Messages, fromProbePoll: false);
                if (newCount == 0 && result.Messages.Count > _lastDomMessageCount)
                {
                    _log.LogInfo(
                        $"[whatsapp] DOM tail grew ({_lastDomMessageCount}→{result.Messages.Count}) without forwards — tail re-poll");
                    await Task.Delay(450, cancellationToken).ConfigureAwait(false);
                    var retry = await PollChatReliableAsync(cancellationToken).ConfigureAwait(false);
                    if (retry?.Status == "ok")
                    {
                        ProcessNewMessages(retry.Messages, fromProbePoll: false);
                    }
                }

                _lastDomMessageCount = result.Messages.Count;
                return;
            default:
                MaybeLogPollHeartbeat(result.Status);
                MaybeRaiseStalled(result.Status);
                return;
        }
    }

    private async Task<WhatsAppWebPollResult?> PollChatReliableAsync(CancellationToken cancellationToken)
    {
        WhatsAppWebPollResult? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await _reader.ExecuteJsonAsync<WhatsAppWebSimpleResult>(
                WhatsAppWebScriptBuilder.BuildScrollToBottomScript(),
                cancellationToken).ConfigureAwait(false);

            if (attempt > 0)
            {
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }

            last = await _reader.ExecuteJsonAsync<WhatsAppWebPollResult>(
                WhatsAppWebScriptBuilder.BuildPollScript(_contactName, _textMarker, forBaseline: false),
                cancellationToken).ConfigureAwait(false);

            if (last?.Status == "ok")
            {
                return last;
            }

            if (last?.Status is not ("loading" or "opening"))
            {
                return last;
            }
        }

        return last;
    }

    private void MaybeLogPollHeartbeat(string status)
    {
        if (string.Equals(_lastPollStatus, status, StringComparison.Ordinal)
            && _pollTicks - _lastLoggedPollStatusTick < 30)
        {
            return;
        }

        _lastPollStatus = status;
        _lastLoggedPollStatusTick = _pollTicks;
        _log.LogInfo($"[whatsapp] Poll heartbeat tick={_pollTicks}, status={status}, baseline={_baselineDone}");
    }

    private int ProcessNewMessages(IReadOnlyList<WhatsAppWebPollMessage> messages, bool fromProbePoll)
    {
        var newCount = 0;
        var skippedDuplicate = 0;
        var skippedFilter = 0;
        var skippedShort = 0;
        var skippedNoStructure = 0;
        var skippedProbe = 0;
        var skippedOutgoing = 0;
        var incomingCandidates = 0;

        string? lastSkippedShortPreview = null;

        foreach (var m in messages)
        {
            var text = (m.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                skippedNoStructure++;
                continue;
            }

            var unstableId = IsUnstableRawId(m.Id);
            var id = NormalizeId(m);

            if (IsProbeMessage(text))
            {
                MarkMessageSeen(id, text, unstableId);
                skippedProbe++;
                TryCompleteProbeFromMessage(text);
                continue;
            }

            if (text.Length < _minForwardTextLength)
            {
                skippedShort++;
                lastSkippedShortPreview = text;
                continue;
            }

            if (!m.HasMessageStructure)
            {
                skippedNoStructure++;
            }

            if (m.IsIncoming)
            {
                incomingCandidates++;
            }

            if (!MatchesMarkerFilter(text))
            {
                skippedFilter++;
                continue;
            }

            if (IsMessageSeen(id, text, unstableId, out var dupReason))
            {
                skippedDuplicate++;
                if (m.IsIncoming && text.Length <= 4)
                {
                    var dupPreview = text.Length <= 24 ? text : text[..24] + "…";
                    _log.LogInfo(
                        $"[whatsapp] Dup skip ({dupReason}, incoming): «{dupPreview}» id={FormatIdForLog(m.Id, id)}");
                }

                continue;
            }

            MarkMessageSeen(id, text, unstableId);

            var msg = new WhatsAppMessage
            {
                Id = id,
                Text = text,
                FromName = _contactName,
                DetectedAt = DateTimeOffset.Now,
            };

            var preview = text.Length <= 48 ? text : text[..48] + "…";
            _log.LogInfo($"[whatsapp] New message ({text.Length} chars, incoming={m.IsIncoming}): «{preview}»");
            newCount++;
            MessageReceived?.Invoke(msg);
        }

        if (newCount > 0)
        {
            _log.LogInfo($"[whatsapp] Forwarded {newCount} new message(s) to Hermes chat");
            _ = _seenStore?.FlushAsync() ?? Task.CompletedTask;
        }
        else if (messages.Count > 0 && _pollTicks % 15 == 0 && !fromProbePoll)
        {
            var shortHint = skippedShort > 0 && lastSkippedShortPreview is not null
                ? $", lastShort=«{lastSkippedShortPreview}» (min={_minForwardTextLength})"
                : string.Empty;
            _log.LogInfo(
                $"[whatsapp] Poll ok: dom={messages.Count}, incoming={incomingCandidates}, dup={skippedDuplicate}, out={skippedOutgoing}, filter={skippedFilter}, short={skippedShort}{shortHint}, probe={skippedProbe}, noStruct={skippedNoStructure}");
        }

        return newCount;
    }

    private static bool IsProbeMessage(string text) =>
        text.StartsWith(ParseProbePrefix, StringComparison.Ordinal);

    private bool MatchesMarkerFilter(string text)
    {
        if (string.IsNullOrWhiteSpace(_textMarker))
        {
            return true;
        }

        return text.Contains(_textMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeId(WhatsAppWebPollMessage m)
    {
        var id = (m.Id ?? string.Empty).Trim();
        if (id.Length > 0 && !id.StartsWith("hash:", StringComparison.Ordinal))
        {
            return id;
        }

        var payload = id.Length > 5 ? id : $"hash:|{m.Text ?? string.Empty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string TextFingerprint(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string FingerprintKey(string fingerprint) => $"fp:{fingerprint}";

    /// <summary>WhatsApp <c>data-id</c> is stable; synthetic <c>hash:</c> ids are not.</summary>
    private static bool IsUnstableRawId(string? rawId)
    {
        var id = (rawId ?? string.Empty).Trim();
        return id.Length == 0 || id.StartsWith("hash:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatIdForLog(string? rawId, string normalizedId)
    {
        var raw = (rawId ?? string.Empty).Trim();
        if (raw.Length > 0 && !raw.StartsWith("hash:", StringComparison.OrdinalIgnoreCase))
        {
            return raw.Length <= 20 ? raw : raw[..20] + "…";
        }

        return normalizedId.Length <= 12 ? normalizedId : normalizedId[..12] + "…";
    }

    private bool IsMessageSeen(string normalizedId, string text, bool unstableId) =>
        IsMessageSeen(normalizedId, text, unstableId, out _);

    private bool IsMessageSeen(string normalizedId, string text, bool unstableId, out string reason)
    {
        if (_seenIds.Contains(normalizedId))
        {
            reason = "id";
            return true;
        }

        if (!unstableId)
        {
            reason = string.Empty;
            return false;
        }

        var fingerprint = TextFingerprint(text);
        if (fingerprint.Length > 0 && _seenIds.Contains(FingerprintKey(fingerprint)))
        {
            reason = "fp";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private void MarkMessageSeen(string normalizedId, string text, bool unstableId)
    {
        _seenIds.Add(normalizedId);
        _seenStore?.Add(normalizedId);

        if (!unstableId)
        {
            return;
        }

        var fingerprint = TextFingerprint(text);
        if (fingerprint.Length == 0)
        {
            return;
        }

        _seenIds.Add(FingerprintKey(fingerprint));
        _seenStore?.Add(FingerprintKey(fingerprint));
    }

    private void RaiseStatus(string text) => StatusChanged?.Invoke(text);

    public async ValueTask DisposeAsync()
    {
        StopMonitoring();
        if (_seenStore is not null)
        {
            await _seenStore.FlushAsync().ConfigureAwait(false);
        }

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }
    }
}
