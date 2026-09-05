using System.IO;
using System.Linq;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class ProjectRenameResult
{
    public required HermesProject Project { get; init; }
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public required string OldPath { get; init; }
    public required string NewPath { get; init; }
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Renames a project folder and migrates name-keyed stores (history, CLI session, logs).
/// Leaves ~/.hermes memories/skills untouched (global; context stays via moved AGENTS.md + hermes/).
/// </summary>
public sealed class ProjectRenameService
{
    private readonly ProjectService _projects;
    private readonly HistoryService _history;
    private readonly HermesCliSessionStore _cliSessions;
    private readonly LogService _log;

    public ProjectRenameService(
        ProjectService projects,
        HistoryService history,
        HermesCliSessionStore cliSessions,
        LogService log)
    {
        _projects = projects;
        _history = history;
        _cliSessions = cliSessions;
        _log = log;
    }

    public static string SanitizeFolderName(string raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Windows forbids : * ? " < > | \ /
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == ':')
            {
                sb.Append(" -");
                continue;
            }

            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        var cleaned = string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Trim().TrimEnd('.', ' ');
    }

    public ProjectRenameResult Rename(HermesProject project, string desiredName)
    {
        ArgumentNullException.ThrowIfNull(project);
        var oldPath = project.WindowsPath.Trim();
        var oldName = project.Name;
        if (!Directory.Exists(oldPath))
            throw new DirectoryNotFoundException("Папка проекта не найдена: " + oldPath);

        var newName = SanitizeFolderName(desiredName);
        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("Укажите новое имя проекта.");

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(oldPath), newName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Новое имя совпадает с текущим.");

        var parent = Path.GetDirectoryName(oldPath)
                     ?? throw new InvalidOperationException("Не удалось определить родительскую папку.");
        var newPath = Path.Combine(parent, newName);
        if (Directory.Exists(newPath))
            throw new InvalidOperationException("Папка уже существует: " + newPath);

        var notes = new StringBuilder();

        // Move project tree (AGENTS.md, hermes/, scripts, memory-on-disk).
        Directory.Move(oldPath, newPath);
        notes.AppendLine("folder: " + oldPath + " → " + newPath);

        MigrateHistory(oldName, newName, notes);
        MigrateCliSession(oldName, newName, notes);

        var oldSessionLogs = Path.Combine(HermesLogPaths.LogsRoot, "Hermes.Wpf", HermesLogPaths.SanitizeProjectFolderName(oldName));
        var newSessionLogs = Path.Combine(HermesLogPaths.LogsRoot, "Hermes.Wpf", HermesLogPaths.SanitizeProjectFolderName(newName));
        MigrateLogFolder(oldSessionLogs, newSessionLogs, notes, "session-logs");
        // Open session file lived under oldSessionLogs — retarget before any further Log* calls.
        _log.RetargetAfterProjectRename(oldName, newName);

        var oldChatLogs = Path.Combine(HermesLogPaths.ChatLogsRoot, HermesLogPaths.SanitizeProjectFolderName(oldName));
        var newChatLogs = Path.Combine(HermesLogPaths.ChatLogsRoot, HermesLogPaths.SanitizeProjectFolderName(newName));
        MigrateLogFolder(oldChatLogs, newChatLogs, notes, "chat-logs");

        MaybeRenameSideFile(newPath, oldName, newName, notes);
        TouchProjectScopeNote(newPath, oldName, newName, notes);

        var renamed = _projects.BuildProject(newPath);
        _log.LogInfo("[project] Renamed " + oldName + " → " + renamed.Name);
        return new ProjectRenameResult
        {
            Project = renamed,
            OldName = oldName,
            NewName = renamed.Name,
            OldPath = oldPath,
            NewPath = newPath,
            Summary = notes.ToString().Trim(),
        };
    }

    private void MigrateHistory(string oldName, string newName, StringBuilder notes)
    {
        try
        {
            var oldFile = _history.GetHistoryFilePath(oldName);
            var newFile = _history.GetHistoryFilePath(newName);
            if (!File.Exists(oldFile))
            {
                notes.AppendLine("history: (none)");
                return;
            }

            if (string.Equals(oldFile, newFile, StringComparison.OrdinalIgnoreCase))
            {
                // Same sanitized filename — rewrite ProjectName inside.
                RewriteHistoryProjectName(oldFile, newName);
                notes.AppendLine("history: updated ProjectName in place");
                return;
            }

            if (File.Exists(newFile))
                File.Delete(newFile);

            File.Move(oldFile, newFile);
            RewriteHistoryProjectName(newFile, newName);
            notes.AppendLine("history: " + Path.GetFileName(oldFile) + " → " + Path.GetFileName(newFile));
        }
        catch (Exception ex)
        {
            notes.AppendLine("history FAIL: " + ex.Message);
            _log.LogWarn("[project] history migrate: " + ex.Message);
        }
    }

    private static void RewriteHistoryProjectName(string historyFile, string newName)
    {
        try
        {
            var json = File.ReadAllText(historyFile);
            // Lightweight replace of JSON property value for ProjectName.
            var updated = System.Text.RegularExpressions.Regex.Replace(
                json,
                "\"ProjectName\"\\s*:\\s*\"[^\"]*\"",
                "\"ProjectName\": \"" + EscapeJson(newName) + "\"");
            if (!string.Equals(json, updated, StringComparison.Ordinal))
                File.WriteAllText(historyFile, updated);
        }
        catch
        {
            // non-fatal
        }
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private void MigrateCliSession(string oldName, string newName, StringBuilder notes)
    {
        try
        {
            var id = _cliSessions.GetSessionId(oldName);
            if (string.IsNullOrWhiteSpace(id))
            {
                notes.AppendLine("cli_session: (none)");
                return;
            }

            _cliSessions.SetSessionId(newName, id);
            _cliSessions.ClearSessionId(oldName);
            notes.AppendLine("cli_session: key " + oldName + " → " + newName);
        }
        catch (Exception ex)
        {
            notes.AppendLine("cli_session FAIL: " + ex.Message);
            _log.LogWarn("[project] cli_session migrate: " + ex.Message);
        }
    }

    private static void MigrateLogFolder(string oldDir, string newDir, StringBuilder notes, string label)
    {
        try
        {
            if (!Directory.Exists(oldDir))
            {
                notes.AppendLine(label + ": (none)");
                return;
            }

            if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase))
            {
                notes.AppendLine(label + ": same path");
                return;
            }

            if (Directory.Exists(newDir))
            {
                foreach (var file in Directory.GetFiles(oldDir))
                {
                    var dest = Path.Combine(newDir, Path.GetFileName(file));
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(file, dest);
                }

                try { Directory.Delete(oldDir, recursive: true); } catch { /* ignore */ }
            }
            else
            {
                var parent = Path.GetDirectoryName(newDir);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                Directory.Move(oldDir, newDir);
            }

            notes.AppendLine(label + ": moved");
        }
        catch (Exception ex)
        {
            notes.AppendLine(label + " FAIL: " + ex.Message);
        }
    }

    private static void MaybeRenameSideFile(string projectPath, string oldName, string newName, StringBuilder notes)
    {
        try
        {
            var oldTxt = Path.Combine(projectPath, oldName + ".txt");
            var newTxt = Path.Combine(projectPath, newName + ".txt");
            if (File.Exists(oldTxt) && !File.Exists(newTxt))
            {
                File.Move(oldTxt, newTxt);
                notes.AppendLine("side-file: " + oldName + ".txt → " + newName + ".txt");
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TouchProjectScopeNote(string projectPath, string oldName, string newName, StringBuilder notes)
    {
        try
        {
            var projectMd = Path.Combine(projectPath, "hermes", "project.md");
            if (!File.Exists(projectMd))
                return;

            var text = File.ReadAllText(projectMd);
            if (text.IndexOf("## Переименование", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            var stamp = DateTime.Now.ToString("yyyy-MM-dd");
            var block =
                Environment.NewLine
                + "## Переименование" + Environment.NewLine
                + Environment.NewLine
                + $"- Было: `{oldName}` → стало: `{newName}` ({stamp})." + Environment.NewLine
                + "- Папка на диске, `AGENTS.md` и `hermes/` перенесены целиком." + Environment.NewLine
                + "- Память и skills в `~/.hermes/` глобальные и сохранены без изменений." + Environment.NewLine;

            File.AppendAllText(projectMd, block);
            notes.AppendLine("project.md: rename note appended");
        }
        catch
        {
            // ignore
        }
    }
}
