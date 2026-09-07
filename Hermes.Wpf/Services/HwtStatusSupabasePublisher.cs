using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Publishes HermesWpfTerminal status.json to Supabase for Hermes.RemoteTerminal (view-only).
/// On demand only — triggered by Mt5Terminal action <c>refresh</c> (no background loop).
/// </summary>
public sealed class HwtStatusSupabasePublisher : IDisposable
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private readonly Func<SupabaseChatRelayService?> _relayFactory;
    private readonly Func<string?> _projectPathFactory;

    public HwtStatusSupabasePublisher(
        LogService log,
        HermesSettings settings,
        Func<SupabaseChatRelayService?> relayFactory,
        Func<string?> projectPathFactory)
    {
        _log = log;
        _settings = settings;
        _relayFactory = relayFactory;
        _projectPathFactory = projectPathFactory;
    }

    /// <summary>No-op: auto-publish loop removed; use <see cref="PublishNowAsync"/>.</summary>
    public void Start()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Force-publish current HWT status.json to RemoteTerminal (ignores hash dedupe).
    /// </summary>
    public async Task<(bool Ok, string Message)> PublishNowAsync(CancellationToken ct = default)
    {
        if (!_settings.SupabaseRelayEnabled)
        {
            return (false, "Supabase Relay выключен");
        }

        if (!_settings.SupabaseHwtStatusPublishEnabled)
        {
            return (false, "Публикация HWT status выключена в настройках");
        }

        var relay = _relayFactory();
        if (relay is not { IsConnected: true })
        {
            return (false, "Supabase relay не подключён");
        }

        var raw = Mt5TerminalIpcClient.TryReadStatusJson(_projectPathFactory());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (false, "status.json недоступен (HermesWpfTerminal открыт?)");
        }

        var payload = BuildPayload(raw);
        if (payload is null)
        {
            return (false, "не удалось собрать hwt_status payload");
        }

        var sender = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogSenderName)
            ? "Hermes"
            : _settings.SupabaseRemoteLogSenderName.Trim();
        var recipient = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogRecipientName)
            ? "RemoteTerminal"
            : _settings.SupabaseRemoteLogRecipientName.Trim();

        try
        {
            await relay.EnsureFreshSessionAsync(ct).ConfigureAwait(false);
            await relay.InsertAssistantRowAsync(sender, recipient, payload, ct, logPublish: false)
                .ConfigureAwait(false);
            _log.LogInfo("[hwt-status] published on refresh → " + recipient);
            return (true, "опубликовано → " + recipient);
        }
        catch (Exception ex)
        {
            _log.LogWarn("[hwt-status] refresh publish: " + ex.Message);
            return (false, ex.Message);
        }
    }

    private static string? BuildPayload(string statusJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(statusJson);
            var root = doc.RootElement;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "hwt_status");
                CopyString(writer, root, "utc");
                CopyString(writer, root, "note");
                CopyString(writer, root, "build");
                CopyString(writer, root, "symbol");
                CopyString(writer, root, "bid");
                CopyString(writer, root, "ask");
                CopyString(writer, root, "lot");
                CopyString(writer, root, "account");
                CopyString(writer, root, "market_status");
                CopyString(writer, root, "positions_header");
                if (root.TryGetProperty("real_trading", out var rt))
                {
                    writer.WriteBoolean("real_trading", rt.ValueKind == JsonValueKind.True);
                }

                if (root.TryGetProperty("auto_trade", out var at))
                {
                    writer.WriteBoolean("auto_trade", at.ValueKind == JsonValueKind.True);
                }

                if (root.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName("positions");
                    positions.WriteTo(writer);
                }

                if (root.TryGetProperty("pending_orders", out var pendingOrders) && pendingOrders.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName("pending_orders");
                    pendingOrders.WriteTo(writer);
                }
                else if (root.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName("pending_orders");
                    pending.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static void CopyString(Utf8JsonWriter writer, JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            writer.WriteString(name, el.GetString());
        }
    }
}
