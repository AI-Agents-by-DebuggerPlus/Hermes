using System.Globalization;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Models.Biohacker;

/// <summary>Tiny YAML scalar/list reader for Biohacker vault files.</summary>
internal static class BiohackerYaml
{
    internal static Dictionary<string, string> ReadFrontmatter(string rawMarkdown)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!ExternalBrainMarkdown.TrySplitYamlFrontmatter(rawMarkdown ?? string.Empty, out var yamlBlock, out _))
        {
            return dict;
        }

        foreach (var rawLine in yamlBlock.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var val = line[(colon + 1)..].Trim();
            dict[key] = val;
        }

        return dict;
    }

    internal static string Str(Dictionary<string, string> d, string key)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return string.Empty;
        }

        return v.Trim().Trim('"', '\'').Trim();
    }

    internal static int Int(Dictionary<string, string> d, string key, int @default = 0)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return @default;
        }

        v = v.Trim().Trim('"', '\'');
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : @default;
    }

    internal static int? IntOpt(Dictionary<string, string> d, string key)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return null;
        }

        v = v.Trim().Trim('"', '\'');
        if (v.Length == 0)
        {
            return null;
        }

        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    internal static DateTime DateTimeUtc(Dictionary<string, string> d, string key, DateTime @default)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return @default;
        }

        v = v.Trim().Trim('"', '\'');
        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return @default;
    }

    internal static DateTime? DateTimeUtcOpt(Dictionary<string, string> d, string key)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return null;
        }

        v = v.Trim().Trim('"', '\'');
        if (v.Length == 0)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return null;
    }

    internal static List<string> List(Dictionary<string, string> d, string key)
    {
        if (!d.TryGetValue(key, out var v))
        {
            return [];
        }

        v = v.Trim();
        if (v.Length == 0)
        {
            return [];
        }

        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            return v[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Trim().Trim('"', '\'').Trim())
                .Where(p => p.Length > 0)
                .ToList();
        }

        return [v.Trim().Trim('"', '\'').Trim()];
    }

    internal static string YamlString(string? s)
    {
        var v = (s ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Trim();
        return v;
    }

    internal static string YamlList(IEnumerable<string> items)
    {
        var clean = items
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => $"\"{s.Replace("\"", "'")}\"");
        return "[" + string.Join(", ", clean) + "]";
    }

    internal static string IsoUtc(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            dt = dt.ToUniversalTime();
        }

        return dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
