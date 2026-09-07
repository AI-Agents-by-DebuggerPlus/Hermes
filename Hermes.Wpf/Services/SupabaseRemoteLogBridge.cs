using System.Collections.Concurrent;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Mirrors selected Hermes.Wpf log lines to Supabase for <c>RemoteTerminal.Xp</c> (view-only backup bridge).
/// </summary>
public sealed class SupabaseRemoteLogBridge : IDisposable
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private readonly Func<SupabaseChatRelayService?> _relayFactory;
    private readonly ConcurrentQueue<(string Level, string Line)> _queue = new();
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private bool _subscribed;
    private int _dropping;

    public SupabaseRemoteLogBridge(
        LogService log,
        HermesSettings settings,
        Func<SupabaseChatRelayService?> relayFactory)
    {
        _log = log;
        _settings = settings;
        _relayFactory = relayFactory;
    }

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        _log.LineLogged += OnLineLogged;
        _subscribed = true;
        _cts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            _log.LineLogged -= OnLineLogged;
            _subscribed = false;
        }

        try
        {
            _cts?.Cancel();
            _pumpTask?.Wait(2000);
        }
        catch
        {
            // ignore
        }

        _cts?.Dispose();
        _cts = null;
    }

    private void OnLineLogged(string level, string line)
    {
        if (!_settings.SupabaseRemoteLogEnabled || !_settings.SupabaseRelayEnabled)
        {
            return;
        }

        if (!ShouldMirror(level, line))
        {
            return;
        }

        // Bound queue — drop oldest-style by skipping when flooded.
        if (_queue.Count > 80)
        {
            if (Interlocked.Exchange(ref _dropping, 1) == 0)
            {
                // Avoid recursion: do not LogInfo here.
            }

            return;
        }

        Interlocked.Exchange(ref _dropping, 0);
        _queue.Enqueue((level, line));
    }

    private bool ShouldMirror(string level, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // Prevent echo loops from our own publishes / supabase chatter.
        if (line.Contains("[supabase-remote-log]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("[LOG:HermesWpf]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("[supabase] Published row", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(level, "INFO", StringComparison.OrdinalIgnoreCase)
            && !_settings.SupabaseRemoteLogIncludeInfo)
        {
            return false;
        }

        return true;
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var item))
                {
                    await PublishAsync(item.Line, ct).ConfigureAwait(false);
                    await Task.Delay(350, ct).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task PublishAsync(string line, CancellationToken ct)
    {
        var relay = _relayFactory();
        if (relay is not { IsConnected: true })
        {
            return;
        }

        var sender = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogSenderName)
            ? "Hermes"
            : _settings.SupabaseRemoteLogSenderName.Trim();
        var recipient = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogRecipientName)
            ? "RemoteTerminal"
            : _settings.SupabaseRemoteLogRecipientName.Trim();

        var content = "[LOG:HermesWpf] " + line;
        try
        {
            await relay.EnsureFreshSessionAsync(ct).ConfigureAwait(false);
            await relay.InsertAssistantRowAsync(sender, recipient, content, ct, logPublish: false)
                .ConfigureAwait(false);
        }
        catch
        {
            // Silent — remote log is best-effort backup path.
        }
    }
}
