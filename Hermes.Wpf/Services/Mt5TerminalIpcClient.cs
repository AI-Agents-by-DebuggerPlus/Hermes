using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Writes trade commands for HermesWpfTerminal Agent IPC and waits for result.json.
/// </summary>
public sealed class Mt5TerminalIpcClient
{
    private readonly LogService _log;

    public Mt5TerminalIpcClient(LogService log)
    {
        _log = log;
    }

    public static string ResolveIpcDir(string? projectWindowsPath)
    {
        var env = Environment.GetEnvironmentVariable("HERMES_MT5_IPC_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var project = (projectWindowsPath ?? string.Empty).Trim();
        if (project.Length > 0)
        {
            return Path.Combine(project, "hermes", "ipc");
        }

        return @"D:\Programming\AI_Agents\HermesProjects\Mt5Terminal\hermes\ipc";
    }

    public static string? TryReadChartSymbol(string? projectWindowsPath)
    {
        var statusPath = Path.Combine(ResolveIpcDir(projectWindowsPath), "status.json");
        if (!File.Exists(statusPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(statusPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("symbol", out var sym)
                && sym.ValueKind == JsonValueKind.String)
            {
                var s = sym.GetString()?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public async Task<Mt5TerminalIpcExecutionResult> ExecuteAsync(
        Mt5TerminalRouteCommand command,
        string? projectWindowsPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var dir = ResolveIpcDir(projectWindowsPath);
        Directory.CreateDirectory(dir);

        var commandPath = Path.Combine(dir, "command.json");
        var resultPath = Path.Combine(dir, "result.json");
        var statusPath = Path.Combine(dir, "status.json");

        // Drop stale result so we don't pick up a previous id.
        try
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[mt5-ipc] could not clear result.json: {ex.Message}");
        }

        var json = command.ToCommandJson();
        var tmp = commandPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        if (File.Exists(commandPath))
        {
            File.Delete(commandPath);
        }

        File.Move(tmp, commandPath);
        _log.LogInfo($"[mt5-ipc] wrote command id={command.Id} action={command.Action} → {commandPath}");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                string raw;
                try
                {
                    raw = await File.ReadAllTextAsync(resultPath, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (TryReadResult(raw, out var parsed)
                    && string.Equals(parsed.Id, command.Id, StringComparison.Ordinal))
                {
                    parsed.StatusJson = SafeRead(statusPath);
                    return parsed;
                }
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return new Mt5TerminalIpcExecutionResult
        {
            Ok = false,
            Id = command.Id,
            Action = command.Action,
            Error = $"timeout waiting for HermesWpfTerminal result.json ({timeout.TotalSeconds:0}s). Is the terminal (v33+) open?",
            StatusJson = SafeRead(statusPath)
        };
    }

    public static string FormatChatMessage(
        Mt5TerminalRouteCommand cmd,
        Mt5TerminalIpcExecutionResult exec,
        string? userMessage = null)
    {
        var action = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is "screenshot" or "chart_screenshot")
        {
            return FormatScreenshotChatMessage(cmd, exec);
        }

        if (action == "place_pending")
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildTtsLeadLine(cmd.Action, exec.Ok));
            sb.AppendLine($"Сигнал Trading Analytics → Mt5Terminal: {cmd.PendingOrderType} {cmd.Symbol}");
            sb.AppendLine(exec.Ok ? "Исполнение: OK (pending order sent to HWT)" : "Исполнение: FAIL");
            if (!string.IsNullOrWhiteSpace(exec.Error))
            {
                sb.AppendLine(exec.Error.Trim());
            }

            if (!string.IsNullOrWhiteSpace(exec.Message))
            {
                sb.AppendLine(exec.Message.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        if (action == "list_symbols")
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildTtsLeadLine(cmd.Action, exec.Ok));
            if (exec.Ok)
            {
                sb.AppendLine($"Mt5Terminal: получен список символов ({exec.SymbolsCount ?? 0}).");
                if (!string.IsNullOrWhiteSpace(exec.SymbolsPath))
                {
                    sb.AppendLine($"Файл: {exec.SymbolsPath}");
                }
            }
            else
            {
                sb.AppendLine("Не удалось получить список символов.");
                if (!string.IsNullOrWhiteSpace(exec.Error))
                {
                    sb.AppendLine(exec.Error.Trim());
                }
            }

            return sb.ToString().TrimEnd();
        }

        if (action is "snapshot" or "status" or "get_status")
        {
            return FormatSnapshotChatMessage(cmd, exec, userMessage);
        }

        var sbDefault = new StringBuilder();
        sbDefault.AppendLine(BuildTtsLeadLine(cmd.Action, exec.Ok));
        sbDefault.AppendLine($"Задача: {cmd.Action}");
        if (exec.Ok)
        {
            sbDefault.AppendLine("Исполнение: OK");
        }
        else
        {
            sbDefault.AppendLine("Исполнение: FAIL");
            if (!string.IsNullOrWhiteSpace(exec.Error))
            {
                sbDefault.AppendLine(exec.Error.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(exec.Message)
            && !string.Equals(exec.Message.Trim(), "accepted", StringComparison.OrdinalIgnoreCase))
        {
            sbDefault.AppendLine(exec.Message.Trim());
        }

        // Trade ops: positions + short MT5 log for verification (no quote noise).
        AppendSnapshotFacts(
            sbDefault,
            exec.SnapshotJson ?? exec.StatusJson,
            SnapshotFactsMode.TradeVerify);
        return sbDefault.ToString().TrimEnd();
    }

    private static string FormatSnapshotChatMessage(
        Mt5TerminalRouteCommand cmd,
        Mt5TerminalIpcExecutionResult exec,
        string? userMessage)
    {
        var sb = new StringBuilder();
        var json = exec.SnapshotJson ?? exec.StatusJson;

        if (!exec.Ok)
        {
            sb.AppendLine(BuildTtsLeadLine(cmd.Action, false));
            if (!string.IsNullOrWhiteSpace(exec.Error))
            {
                sb.AppendLine(exec.Error.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        if (Mt5TerminalTradeRouter.LooksLikeBalanceOnlyRequest(userMessage))
        {
            var account = TryReadSnapshotString(json, "account");
            var spoken = FormatBalanceSpokenRu(account);
            sb.AppendLine("{\"ru\":\"" + EscapeJsonString(spoken) + "\"}");
            if (!string.IsNullOrWhiteSpace(account))
            {
                sb.AppendLine(account);
            }

            return sb.ToString().TrimEnd();
        }

        if (Mt5TerminalTradeRouter.LooksLikePriceOnlyRequest(userMessage))
        {
            var symbol = TryReadSnapshotString(json, "symbol");
            var bid = TryReadSnapshotString(json, "bid");
            var ask = TryReadSnapshotString(json, "ask");
            var spoken = FormatPriceSpokenRu(symbol, bid, ask);
            sb.AppendLine("{\"ru\":\"" + EscapeJsonString(spoken) + "\"}");
            if (!string.IsNullOrWhiteSpace(symbol) || !string.IsNullOrWhiteSpace(bid) || !string.IsNullOrWhiteSpace(ask))
            {
                sb.AppendLine(
                    (symbol ?? "?")
                    + "  bid=" + (bid ?? "?")
                    + "  ask=" + (ask ?? "?"));
            }

            return sb.ToString().TrimEnd();
        }

        sb.AppendLine(BuildTtsLeadLine(cmd.Action, true));
        AppendSnapshotFacts(sb, json, SnapshotFactsMode.Status);
        return sb.ToString().TrimEnd();
    }

    private enum SnapshotFactsMode
    {
        /// <summary>Account + positions + trading flags only.</summary>
        Status,
        /// <summary>Positions + short log_tail for trade confirmation.</summary>
        TradeVerify,
    }

    /// <summary>Short chat: TTS notice + file link only.</summary>
    private static string FormatScreenshotChatMessage(Mt5TerminalRouteCommand cmd, Mt5TerminalIpcExecutionResult exec)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildTtsLeadLine(cmd.Action, exec.Ok));
        if (!exec.Ok)
        {
            if (!string.IsNullOrWhiteSpace(exec.Error))
            {
                sb.AppendLine(exec.Error.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        var link = !string.IsNullOrWhiteSpace(exec.ScreenshotLink)
            ? exec.ScreenshotLink.Trim()
            : null;
        if (link is null && !string.IsNullOrWhiteSpace(exec.ScreenshotPath))
        {
            try
            {
                link = new Uri(exec.ScreenshotPath.Trim()).AbsoluteUri;
            }
            catch
            {
                link = exec.ScreenshotPath.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(link))
        {
            sb.AppendLine(link);
        }

        // Optional one-line remote publish note (no snapshot dump).
        if (!string.IsNullOrWhiteSpace(exec.Message)
            && exec.Message.Contains("RemoteTerminal", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in exec.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Contains("RemoteTerminal", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("отправлено", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(line);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// AndroidChat TTS protocol: speak only <c>{"ru":…}</c> / <c>{"en":…}</c>; trailing lines are silent info.
    /// </summary>
    private static string BuildTtsLeadLine(string action, bool ok)
    {
        var a = (action ?? string.Empty).Trim().ToLowerInvariant();
        string ru;
        if (!ok)
        {
            ru = a switch
            {
                "screenshot" or "chart_screenshot" => "не удалось сделать скриншот",
                _ => "задача не выполнена",
            };
        }
        else
        {
            ru = a switch
            {
                "screenshot" or "chart_screenshot" => "скриншот создан",
                "refresh" => "удалённый терминал обновлён",
                "snapshot" or "status" or "get_status" => "статус получен",
                "buy_market" or "buy" => "покупка отправлена",
                "sell_market" or "sell" => "продажа отправлена",
                "close_all" => "закрытие всех позиций отправлено",
                "close_slot" => "закрытие позиции отправлено",
                "set_lot" => "лот изменён",
                "set_real_trading" => "режим торговли обновлён",
                "set_auto_trade" => "автоторговля обновлена",
                _ => "задача выполнена",
            };
        }

        return "{\"ru\":\"" + EscapeJsonString(ru) + "\"}";
    }

    private static string EscapeJsonString(string s) =>
        (s ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// HWT account line: "Balance: 1234.56   Equity: …   USD" → spoken "Баланс 1234.56 USD".
    /// </summary>
    private static string FormatBalanceSpokenRu(string? accountLine)
    {
        var raw = (accountLine ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return "баланс недоступен";
        }

        var m = Regex.Match(
            raw,
            @"Balance:\s*([0-9]+(?:[.,][0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return "баланс: " + raw;
        }

        var value = m.Groups[1].Value.Replace(',', '.');
        var currency = string.Empty;
        var cur = Regex.Match(raw, @"\b([A-Z]{3})\s*$");
        if (cur.Success)
        {
            currency = cur.Groups[1].Value;
        }

        return string.IsNullOrEmpty(currency)
            ? "Баланс " + value
            : "Баланс " + value + " " + currency;
    }

    private static string FormatPriceSpokenRu(string? symbol, string? bid, string? ask)
    {
        var sym = (symbol ?? string.Empty).Trim();
        var b = (bid ?? string.Empty).Trim();
        var a = (ask ?? string.Empty).Trim();
        if (sym.Length == 0 && b.Length == 0 && a.Length == 0)
        {
            return "цена недоступна";
        }

        if (b.Length > 0 && a.Length > 0)
        {
            return string.IsNullOrEmpty(sym)
                ? "Цена bid " + b + ", ask " + a
                : sym + ": bid " + b + ", ask " + a;
        }

        var one = b.Length > 0 ? b : a;
        if (one.Length > 0)
        {
            return string.IsNullOrEmpty(sym) ? "Цена " + one : sym + ": " + one;
        }

        return string.IsNullOrEmpty(sym) ? "цена недоступна" : "Цена " + sym + " недоступна";
    }

    private static void AppendSnapshotFacts(StringBuilder sb, string? json, SnapshotFactsMode mode)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
            {
                root = snap;
            }

            void AppendString(string name, string label)
            {
                if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s))
                    {
                        sb.AppendLine(label + s);
                    }
                }
            }

            if (mode == SnapshotFactsMode.Status)
            {
                AppendString("account", "account: ");
                if (root.TryGetProperty("real_trading", out var rtStatus))
                {
                    sb.AppendLine("real_trading=" + (rtStatus.ValueKind == JsonValueKind.True ? "true" : "false"));
                }

                if (root.TryGetProperty("positions_header", out var phStatus) && phStatus.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine("positions: " + phStatus.GetString());
                }

                if (root.TryGetProperty("positions", out var posStatus) && posStatus.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in posStatus.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.String)
                        {
                            sb.AppendLine("  - " + p.GetString());
                        }
                    }
                }

                return;
            }

            // TradeVerify
            AppendString("account", "account: ");
            if (root.TryGetProperty("real_trading", out var rt))
            {
                sb.AppendLine("real_trading=" + (rt.ValueKind == JsonValueKind.True ? "true" : "false"));
            }

            if (root.TryGetProperty("positions_header", out var ph) && ph.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine("positions: " + ph.GetString());
            }

            if (root.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in positions.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine("  - " + p.GetString());
                    }
                }
            }

            if (root.TryGetProperty("log_tail", out var logs) && logs.ValueKind == JsonValueKind.Array)
            {
                var lines = logs.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => x.Length > 0)
                    .TakeLast(6)
                    .ToList();
                if (lines.Count > 0)
                {
                    sb.AppendLine("MT5 log:");
                    foreach (var line in lines)
                    {
                        sb.AppendLine("  " + line);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore snapshot parse errors
        }
    }

    private static string? TryReadSnapshotString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
            {
                root = snap;
            }

            if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString()?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryReadResult(string raw, out Mt5TerminalIpcExecutionResult result)
    {
        result = new Mt5TerminalIpcExecutionResult();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            result.Ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            result.Id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
            result.Action = root.TryGetProperty("action", out var aEl) && aEl.ValueKind == JsonValueKind.String
                ? aEl.GetString() ?? string.Empty
                : string.Empty;
            result.Message = root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString()
                : null;
            result.Error = root.TryGetProperty("error", out var eEl) && eEl.ValueKind == JsonValueKind.String
                ? eEl.GetString()
                : null;
            if (root.TryGetProperty("snapshot", out var snap))
            {
                result.SnapshotJson = snap.GetRawText();
            }

            if (root.TryGetProperty("screenshot_path", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                result.ScreenshotPath = sp.GetString();
            }

            if (root.TryGetProperty("screenshot_link", out var sl) && sl.ValueKind == JsonValueKind.String)
            {
                result.ScreenshotLink = sl.GetString();
            }

            if (root.TryGetProperty("symbols_path", out var symPath) && symPath.ValueKind == JsonValueKind.String)
            {
                result.SymbolsPath = symPath.GetString();
            }

            if (root.TryGetProperty("symbols_count", out var symCount)
                && symCount.ValueKind == JsonValueKind.Number
                && symCount.TryGetInt32(out var count))
            {
                result.SymbolsCount = count;
            }

            return !string.IsNullOrWhiteSpace(result.Id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Read current HermesWpfTerminal status.json (no command).</summary>
    public static string? TryReadStatusJson(string? projectWindowsPath)
    {
        var path = Path.Combine(ResolveIpcDir(projectWindowsPath), "status.json");
        return SafeRead(path);
    }
}

public sealed class Mt5TerminalIpcExecutionResult
{
    public bool Ok { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? SnapshotJson { get; set; }
    public string? StatusJson { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ScreenshotLink { get; set; }
    public string? SymbolsPath { get; set; }
    public int? SymbolsCount { get; set; }
}
