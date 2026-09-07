using System.Text.Json;
using Hermes.RemoteTerminal.Models;

namespace Hermes.RemoteTerminal.Services;

/// <summary>
/// REST poll backup bridge — same Supabase channel as <c>RemoteTerminal.Xp</c> (XpHttp/curl + SELECT).
/// Activated only when explicitly enabled and WebSocket is offline (not a silent poll-fallback).
/// </summary>
public sealed class SupabaseRestBackupBridge : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string? _accessToken;
    private Guid? _lastSeenId;
    private bool _baselineDone;

    public SupabaseRestBackupBridge(AppSettings settings)
    {
        _settings = settings;
    }

    public event Action<TerminalLine>? LineReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning { get; private set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.SupabaseUrl)
        && !string.IsNullOrWhiteSpace(_settings.SupabaseAnonKey);

    public void Start()
    {
        if (!IsConfigured || IsRunning)
        {
            return;
        }

        Stop();
        _baselineDone = false;
        _lastSeenId = null;
        _accessToken = null;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        IsRunning = true;
        StatusChanged?.Invoke("REST backup: starting…");
        AppLog.Info("REST backup bridge started (XP channel)");
    }

    public void Stop()
    {
        if (!IsRunning && _loopTask is null)
        {
            return;
        }

        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loopTask?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
        IsRunning = false;
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureTokenAsync(ct).ConfigureAwait(false);
                await PollOnceAsync(ct).ConfigureAwait(false);
                StatusChanged?.Invoke("REST backup: listening (XP channel)…");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _accessToken = null;
                var msg = Truncate(ex.Message, 120);
                AppLog.Warn("REST backup poll: " + msg);
                StatusChanged?.Invoke("REST backup error: " + msg);
            }

            var waitSec = Math.Clamp(_settings.RestBackupPollSeconds, 3, 120);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(waitSec), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var attempts = new (string Url, string Body)[]
        {
            (baseUrl + "/auth/v1/signup", "{\"data\":{}}"),
            (baseUrl + "/auth/v1/token?grant_type=anonymous", "{}"),
        };

        foreach (var (url, body) in attempts)
        {
            try
            {
                var text = await BridgeHttp.PostJsonAsync(url, body, anon, anon, ct: ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(text);
                var token = doc.RootElement.TryGetProperty("access_token", out var tok)
                    ? tok.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                _accessToken = token;
                AppLog.Info("REST backup: Supabase session OK (view-only)");
                StatusChanged?.Invoke("REST backup: session OK");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn("REST backup auth: " + Truncate(ex.Message, 100));
            }
        }

        _accessToken = anon;
        StatusChanged?.Invoke("REST backup: anon key (SELECT)");
        AppLog.Warn("REST backup: anonymous auth failed — anon SELECT");
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var url = baseUrl
                  + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at"
                  + "&order=created_at.desc&limit=40";

        string text;
        try
        {
            text = await BridgeHttp.GetAsync(url, anon, _accessToken ?? anon, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("401", StringComparison.Ordinal))
        {
            _accessToken = null;
            StatusChanged?.Invoke("REST backup: 401 — reconnect…");
            return;
        }

        using var doc = JsonDocument.Parse(text);
        var rows = new List<JsonElement>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(el.Clone());
        }

        if (!_baselineDone)
        {
            if (rows.Count > 0 && Guid.TryParse(GetStr(rows[0], "id"), out var id) && id != Guid.Empty)
            {
                _lastSeenId = id;
            }

            _baselineDone = true;
            StatusChanged?.Invoke("REST backup: baseline OK");
            return;
        }

        var fresh = new List<JsonElement>();
        foreach (var row in rows)
        {
            Guid.TryParse(GetStr(row, "id"), out var rowId);
            if (_lastSeenId.HasValue && rowId == _lastSeenId.Value)
            {
                break;
            }

            fresh.Add(row);
        }

        if (rows.Count > 0 && Guid.TryParse(GetStr(rows[0], "id"), out var newest) && newest != Guid.Empty)
        {
            _lastSeenId = newest;
        }

        fresh.Reverse();
        foreach (var row in fresh)
        {
            HandleRow(row);
        }
    }

    private void HandleRow(JsonElement row)
    {
        var recipient = GetStr(row, "recipient_name");
        if (!_settings.ShowAllRecipients
            && !string.Equals(recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var line = new TerminalLine
        {
            Sender = GetStr(row, "sender_name"),
            Recipient = recipient,
            Content = GetStr(row, "content"),
            CreatedAt = GetStr(row, "created_at"),
            MessageId = Guid.TryParse(GetStr(row, "id"), out var id) ? id : null,
        };

        LineReceived?.Invoke(line);
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null
            ? p.ToString()
            : string.Empty;

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s[..n] + "…");
}
