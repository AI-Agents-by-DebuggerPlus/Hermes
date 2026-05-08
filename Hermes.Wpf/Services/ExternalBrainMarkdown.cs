using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

internal readonly record struct BrainYamlFrontmatter(
    string? Type,
    DateTime? TimestampLocal,
    IReadOnlyList<string> YamlTags,
    string? Project,
    int? Importance);

internal static partial class ExternalBrainMarkdown
{
    internal static bool TrySplitYamlFrontmatter(string markdown, out string yamlBlock, out string remainder)
    {
        yamlBlock = string.Empty;
        remainder = markdown ?? string.Empty;
        var text = (markdown ?? string.Empty).ReplaceLineEndings("\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return false;
        }

        var lines = text.Split('\n');
        if (lines.Length < 2)
        {
            return false;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line == "---" || IsAllDashClosingLine(line))
            {
                yamlBlock = string.Join("\n", lines.Skip(1).Take(i - 1));
                remainder = lines.Length > i + 1 ? string.Join("\n", lines.Skip(i + 1)) : string.Empty;
                return true;
            }
        }

        return false;
    }

    internal static BrainYamlFrontmatter ParseYamlBlock(string yamlBlock)
    {
        if (string.IsNullOrWhiteSpace(yamlBlock))
        {
            return new BrainYamlFrontmatter(null, null, Array.Empty<string>(), null, null);
        }

        string? type = null;
        DateTime? ts = null;
        string? project = null;
        int? importance = null;
        List<string>? tagList = null;

        foreach (var rawLine in yamlBlock.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = t.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = t[..colon].Trim();
            var val = t[(colon + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "type":
                    type = NormalizeOneLine(val);
                    break;
                case "timestamp":
                    if (!string.IsNullOrEmpty(val))
                    {
                        ts = ParseTimestamp(val);
                    }

                    break;
                case "tags":
                    tagList = ParseTagsValue(val).ToList();
                    break;
                case "project":
                    project = string.IsNullOrEmpty(val) ? string.Empty : NormalizeOneLine(val);
                    break;
                case "importance":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var imp))
                    {
                        importance = Math.Clamp(imp, 1, 5);
                    }

                    break;
            }
        }

        IReadOnlyList<string> yamlTags = tagList is not null ? tagList : Array.Empty<string>();
        return new BrainYamlFrontmatter(type, ts, yamlTags, project, importance);
    }

    internal static IEnumerable<string> ExtractHashtagTags(string markdown)
    {
        return TagFinder()
            .Matches(markdown ?? string.Empty)
            .Select(m => m.Groups["t"].Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal);
    }

    internal static bool TryGetFilenameTimestamp(string filename, out DateTime localDt)
    {
        localDt = default;
        var stem = Path.GetFileNameWithoutExtension(filename);
        var m = DatePrefix().Match(stem);
        if (!m.Success ||
            !int.TryParse(m.Groups["y"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(m.Groups["mo"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mo) ||
            !int.TryParse(m.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
        {
            return false;
        }

        var hour = m.Groups["H"].Success && int.TryParse(m.Groups["H"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh)
            ? hh
            : 12;
        var min = m.Groups["mi"].Success && int.TryParse(m.Groups["mi"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)
            ? mm
            : 0;
        try
        {
            localDt = new DateTime(y, mo, d, hour, min, 0, DateTimeKind.Local);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes YAML only (keeps leading headings) — used when editing Markdown before save.</summary>
    internal static string MarkdownBodyWithoutYaml(string markdown)
    {
        var text = markdown ?? string.Empty;
        if (!TrySplitYamlFrontmatter(text, out _, out var remainder))
        {
            return text.TrimStart();
        }

        return remainder.TrimStart().ReplaceLineEndings("\n").TrimEnd() + "\n";
    }

    internal static string CleanContentBody(string markdown)
    {
        var text = markdown ?? string.Empty;
        if (TrySplitYamlFrontmatter(text, out _, out var remainder))
        {
            text = remainder;
        }

        text = StripLeadingHeadingLines(text.Trim());
        return CollapseTripleBlank(text);
    }

    private static bool IsAllDashClosingLine(string line) =>
        line.Length >= 3 && line.All(static c => c == '-');

    private static string NormalizeOneLine(string s) => s.Replace("\r", string.Empty).Replace("\n", " ").Trim();

    private static DateTime? ParseTimestamp(string val)
    {
        if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.LocalDateTime;
        }

        if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        }

        return null;
    }

    private static IEnumerable<string> ParseTagsValue(string val)
    {
        val = val.Trim();
        if (val.Length == 0)
        {
            return [];
        }

        if (val.StartsWith('[') && val.EndsWith(']'))
        {
            var inner = val[1..^1];
            return inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static p => p.Trim().Trim('"', '\'').Trim().ToLowerInvariant())
                .Where(static p => p.Length > 0)
                .Distinct(StringComparer.Ordinal);
        }

        return val
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static p => p.ToLowerInvariant())
            .Where(static p => p.Length > 0)
            .Distinct(StringComparer.Ordinal);
    }

    private static string StripLeadingHeadingLines(string text)
    {
        var lines = text.Split('\n').ToList();
        while (lines.Count > 0)
        {
            var t = lines[0].Trim();
            if (t.Length == 0)
            {
                lines.RemoveAt(0);
                continue;
            }

            if (!IsMarkdownHeadingLine(t))
            {
                break;
            }

            lines.RemoveAt(0);
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static bool IsMarkdownHeadingLine(string trimmed)
    {
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
        {
            i++;
        }

        return i <= 6 && i < trimmed.Length && char.IsWhiteSpace(trimmed[i]);
    }

    private static string CollapseTripleBlank(string a)
    {
        return DuplicateNewlines().Replace(a.Trim(), "\n\n").Trim();
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex DuplicateNewlines();

    [GeneratedRegex(@"(?<!\w)#(?<t>[\p{L}_][\w\-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex TagFinder();

    [GeneratedRegex(
        @"^(?<y>\d{4})[\-_](?<mo>\d{2})[\-_](?<d>\d{2})(?:[\-_T](?<H>\d{2})(?<mi>\d{2}))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex DatePrefix();
}
