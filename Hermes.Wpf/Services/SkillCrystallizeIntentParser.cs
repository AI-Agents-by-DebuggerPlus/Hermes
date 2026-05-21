using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

internal static class SkillCrystallizeIntentParser
{
    private static readonly Regex IdRegex = new(@"^[a-z][a-z0-9_]{2,47}$", RegexOptions.CultureInvariant);

    internal static bool TryConsumeSaveIntent(string assistantText, out SkillSavePayload? payload)
    {
        payload = null;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParseSaveJson(json, out var parsed))
            {
                continue;
            }

            payload = parsed;
            return true;
        }

        return TryParseMarkedBlock(assistantText ?? string.Empty, out payload);
    }

    internal static bool TryConsumeRunIntent(string assistantText, out string? skillId)
    {
        skillId = null;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParseRunJson(json, out var id))
            {
                continue;
            }

            skillId = id;
            return true;
        }

        return false;
    }

    internal static string UserFacingSaveLine(SkillSavePayload payload, bool tested, bool testOk) =>
        tested
            ? (testOk
                ? $"[skill] Навык «{payload.Title}» ({payload.Id}) сохранён и прошёл smoke-тест."
                : $"[skill] Навык «{payload.Title}» ({payload.Id}) сохранён, но smoke-тест не прошёл — см. лог.")
            : $"[skill] Навык «{payload.Title}» ({payload.Id}) сохранён в каталог generated skills.";

    internal static string UserFacingRunLine(string skillId, bool ok, string detail) =>
        ok
            ? $"[skill] Запущен навык «{skillId}». {detail}".Trim()
            : $"[skill] Не удалось запустить «{skillId}»: {detail}";

    private static bool TryParseMarkedBlock(string text, out SkillSavePayload? payload)
    {
        payload = null;
        const string begin = "HERMES_SKILL_CRYSTALLIZE_BEGIN";
        const string end = "HERMES_SKILL_CRYSTALLIZE_END";
        var start = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        var endIdx = text.IndexOf(end, start + begin.Length, StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0)
        {
            return false;
        }

        var inner = text[(start + begin.Length)..endIdx].Trim();
        return TryParseSaveJson(inner, out payload);
    }

    private static bool TryParseSaveJson(string json, out SkillSavePayload? payload)
    {
        payload = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk))
            {
                return false;
            }

            if (!string.Equals(sk.GetString()?.Trim(), "skill_save", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var id = ReadString(root, "id");
            if (!IsValidId(id))
            {
                return false;
            }

            var title = ReadString(root, "title");
            if (title.Length == 0)
            {
                title = id;
            }

            var summary = ReadString(root, "summary");
            var kind = ReadString(root, "kind");
            if (kind.Length == 0)
            {
                kind = "prompt";
            }

            kind = kind.ToLowerInvariant();
            if (kind is not ("prompt" or "script" or "intent"))
            {
                kind = "prompt";
            }

            var triggers = ReadStringArray(root, "triggers");
            var scriptBody = ReadString(root, "script_body");
            var ext = ReadString(root, "script_extension");
            if (ext.Length == 0)
            {
                ext = "ps1";
            }

            ext = ext.TrimStart('.').ToLowerInvariant();
            if (ext is not ("ps1" or "py"))
            {
                ext = "ps1";
            }

            payload = new SkillSavePayload(
                id,
                title,
                summary,
                triggers,
                kind,
                scriptBody,
                ext,
                ReadString(root, "outbound_prompt_block"),
                ReadString(root, "test_command"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseRunJson(string json, out string? skillId)
    {
        skillId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk))
            {
                return false;
            }

            if (!string.Equals(sk.GetString()?.Trim(), "run_generated", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var id = ReadString(root, "id");
            if (!IsValidId(id))
            {
                return false;
            }

            skillId = id;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidId(string id) =>
        id.Length >= 3 && IdRegex.IsMatch(id);

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return string.Empty;
        }

        return (el.GetString() ?? string.Empty).Trim();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            var s = (item.GetString() ?? string.Empty).Trim();
            if (s.Length > 0)
            {
                list.Add(s);
            }
        }

        return list;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0 && trimmed.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase))
        {
            yield return trimmed;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            var depth = 0;
            for (var j = i; j < text.Length; j++)
            {
                if (text[j] == '{')
                {
                    depth++;
                }
                else if (text[j] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var slice = text[i..(j + 1)];
                        if (slice.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return slice;
                        }

                        break;
                    }
                }
            }
        }
    }
}
