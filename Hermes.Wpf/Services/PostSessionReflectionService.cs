using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Post-session reflection checkpoint (design §2.2 / plan step 3).</summary>
public sealed class PostSessionReflectionService
{
    public const string ReflectionMarker = "[System / Hermes WPF — reflection checkpoint]";

    private readonly LogService _log;
    private readonly CorrectionLogService _corrections;

    public PostSessionReflectionService(LogService log, CorrectionLogService corrections)
    {
        _log = log;
        _corrections = corrections;
    }

    public IReadOnlyList<CorrectionLogEntry> LoadRecent(
        string projectWindowsPath,
        string? sessionId,
        int maxLines = 80)
    {
        var path = _corrections.GetLogPath(projectWindowsPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var lines = File.ReadAllLines(path);
            var take = lines.Length > maxLines ? lines[^maxLines..] : lines;
            var list = new List<CorrectionLogEntry>();
            foreach (var line in take)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var e = JsonSerializer.Deserialize<CorrectionLogEntry>(line);
                    if (e is null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(sessionId)
                        && !string.IsNullOrWhiteSpace(e.Session)
                        && !string.Equals(e.Session, sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    list.Add(e);
                }
                catch
                {
                    // skip bad line
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[reflection] read corrections failed: {ex.Message}");
            return [];
        }
    }

    public bool ShouldForceTurn(IReadOnlyList<CorrectionLogEntry> corrections, bool nontrivialSession)
    {
        if (corrections.Count >= 2)
        {
            return true;
        }

        if (corrections.Count >= 1 && nontrivialSession)
        {
            return true;
        }

        return nontrivialSession && corrections.Count == 0 && false; // nontrivial alone: wait until we have tool metrics
    }

    public string BuildPrompt(IReadOnlyList<CorrectionLogEntry> corrections)
    {
        var payload = JsonSerializer.Serialize(
            corrections.Select(c => new
            {
                c.Ts,
                c.What,
                c.ActualFirst,
                c.CorrectedTo,
                c.Source,
                c.TaskType,
            }).ToList());

        return
            ReflectionMarker + "\n"
            + "Сессия завершается. По логу коррекций реши строго одно и верни ТОЛЬКО JSON (без markdown):\n"
            + "{\"action\":\"none\",\"reason\":\"...\"}\n"
            + "или (пока не применяем, но можно вернуть для проверки парсера):\n"
            + "{\"action\":\"skill_draft\",\"name\":\"...\",\"content\":\"...\"}\n"
            + "{\"action\":\"memory_fact\",\"text\":\"...\"}\n"
            + "Если коррекций ≥2 — action none без reason недопустим.\n"
            + "Коррекции JSON:\n"
            + payload;
    }

    public void AppendReflectionLog(string projectWindowsPath, object record)
    {
        try
        {
            var dir = HermesProjectLayout.GetHermesDirectory(projectWindowsPath);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "reflection_log.jsonl");
            var line = JsonSerializer.Serialize(record) + "\n";
            File.AppendAllText(path, line);
            _log.LogInfo($"[reflection] logged → {path}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[reflection] log failed: {ex.Message}");
        }
    }
}
