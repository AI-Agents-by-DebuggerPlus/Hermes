using System.IO;
using System.Security.Cryptography;
using System.Text;
using DesktopVoiceChat.Models;

namespace DesktopVoiceChat.Services;

/// <summary>
/// Сообщения из чата, начинающиеся на <c>Diagnostics</c> (диагностика Android-клиента), собираются в файл.
/// <see cref="LogFilePath"/> при каждом новом или изменённом сообщении полностью перезаписывается из кэша.
/// Хэши id сохраняются, чтобы одинаковый контент при Refresh не дублировался.
/// </summary>
public static class AndroidChatLogCapture
{
    private const string LogKeyword = "Diagnostics";

    /// <summary>Основной каталог: логи из чата с Android всегда пишутся сюда (если путь доступен).</summary>
    private const string PinnedAndroidLogsDirectory =
        @"D:\Programming\Cursor\2026\WPF\April\AndroidVoiceClient\android\Logs";

    private static readonly object Sync = new();

    /// <inheritdoc cref="StoredIdsPath" />
    private static readonly Dictionary<Guid, string> StoredContentHashes = new();

    /// <summary>Тексты блоков по id для полной пересборки <see cref="LogFilePath"/>.</summary>
    private static readonly Dictionary<Guid, (DateTimeOffset When, string Block)> CapturedDiagnostics = new();

    private static bool _idsLoaded;

    /// <inheritdoc cref="ResolveLogDirectory" />
    public static string DataDirectory => LogDirectory;

    /// <summary>Каталог для лога и файла известных id (папка создаётся при записи).</summary>
    public static string LogDirectory => ResolvedLogDirectory.Value;

    /// <summary>Тело логов из чата (человекочитаемый текст).</summary>
    public static string LogFilePath => Path.Combine(LogDirectory, "android-client-chat-logs.txt");

    /// <summary>Список уже записанных <c>messages.id</c> (одна строка — один GUID).</summary>
    public static string StoredIdsPath => Path.Combine(LogDirectory, "android-client-log-msg-ids.txt");

    private static readonly Lazy<string> ResolvedLogDirectory = new(ComputeLogDirectory);

    private static string ComputeLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(PinnedAndroidLogsDirectory);
            return PinnedAndroidLogsDirectory;
        }
        catch
        {
            // Если D:\ недоступен — пробуем android/Logs рядом с найденным AndroidVoiceClient, затем %LocalAppData%.
        }

        var nearRepo = TryResolveRepoAndroidLogs();
        if (!string.IsNullOrEmpty(nearRepo))
        {
            return nearRepo;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopVoiceChat",
            "android",
            "Logs");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>Ищет каталог решения по имени <c>AndroidVoiceClient</c> и возвращает <c>android/Logs</c>.</summary>
    private static string? TryResolveRepoAndroidLogs()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 14 && dir != null; i++)
            {
                if (string.Equals(dir.Name, "AndroidVoiceClient", StringComparison.OrdinalIgnoreCase))
                {
                    var logs = Path.Combine(dir.FullName, "android", "Logs");
                    Directory.CreateDirectory(logs);
                    return logs;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // ниже резервный путь
        }

        return null;
    }

    private static void MigrateLegacyPlainAppDataFolderIfNeeded()
    {
        var legacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopVoiceChat");
        var legacyLog = Path.Combine(legacyDir, "android-client-chat-logs.txt");
        var legacyIds = Path.Combine(legacyDir, "android-client-log-msg-ids.txt");

        try
        {
            Directory.CreateDirectory(LogDirectory);

            if (!File.Exists(LogFilePath) && File.Exists(legacyLog))
            {
                File.Copy(legacyLog, LogFilePath, overwrite: false);
                AppLogService.Log("Лог Android перенесён из %LocalAppData%\\DesktopVoiceChat в новый каталог.", "AndroidLog");
            }

            if (!File.Exists(StoredIdsPath) && File.Exists(legacyIds))
            {
                File.Copy(legacyIds, StoredIdsPath, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            AppLogService.Log($"Android log migrate: {ex.Message}", "Error");
        }
    }

    /// <summary>Если <paramref name="message"/> начинается с Diagnostics, добавить блок в кэш и перезаписать <see cref="LogFilePath"/>.</summary>
    public static void TryAppendFromChatMessage(Message message)
    {
        if (string.IsNullOrEmpty(message.Content) ||
            !message.Content.TrimStart().StartsWith(LogKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (Sync)
        {
            MigrateLegacyPlainAppDataFolderIfNeeded();

            EnsureIdsLoaded();
            var contentHash = Sha256Hex(message.Content);
            if (StoredContentHashes.TryGetValue(message.Id, out var existingHash) &&
                string.Equals(existingHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(LogDirectory);
            StoredContentHashes[message.Id] = contentHash;
            PersistIdsSnapshot();

            var when = message.CreatedAt == default ? DateTimeOffset.Now : message.CreatedAt;
            var block = FormatBlock(message);
            CapturedDiagnostics[message.Id] = (when, block);
            RewriteDiagnosticsLogFile();
            AppLogService.Log($"Запись Android-лога в файл ({message.Id}).", "AndroidLog");
        }
    }

    private static void RewriteDiagnosticsLogFile()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var (_, entry) in CapturedDiagnostics.OrderBy(p => p.Value.When).ThenBy(p => p.Key))
            {
                sb.Append(entry.Block);
            }

            File.WriteAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLogService.Log($"Android log file rewrite: {ex.Message}", "Error");
        }
    }

    private static void EnsureIdsLoaded()
    {
        if (_idsLoaded)
        {
            return;
        }

        _idsLoaded = true;
        try
        {
            if (!File.Exists(StoredIdsPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(StoredIdsPath, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // формат: {guid}|{sha256} (или legacy: только guid)
                var parts = trimmed.Split('|', 2);
                if (!Guid.TryParse(parts[0].Trim(), out var id))
                {
                    continue;
                }

                var hash = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                if (!StoredContentHashes.ContainsKey(id))
                {
                    StoredContentHashes[id] = hash;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogService.Log($"Android log ids load: {ex.Message}", "Error");
        }
    }

    private static void PersistIdsSnapshot()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var pair in StoredContentHashes.OrderBy(p => p.Key))
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    sb.Append(pair.Key).AppendLine();
                }
                else
                {
                    sb.Append(pair.Key).Append('|').Append(pair.Value).AppendLine();
                }
            }

            File.WriteAllText(StoredIdsPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLogService.Log($"Android log id persist: {ex.Message}", "Error");
        }
    }

    private static string FormatBlock(Message message)
    {
        var when = message.CreatedAt == default ? DateTimeOffset.Now : message.CreatedAt;

        var sb = new StringBuilder();
        sb.Append("--- ").Append(when.ToString("O")).Append(" | ");
        sb.Append(message.SenderName).Append(" | ").Append(message.Id).AppendLine();
        // сохраняем контент "как есть", чтобы совпадал с сообщением
        sb.Append(message.Content).AppendLine();
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
