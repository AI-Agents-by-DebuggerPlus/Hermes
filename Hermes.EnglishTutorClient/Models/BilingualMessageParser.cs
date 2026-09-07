using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishTutorClient.Models;

/// <summary>One speakable fragment from Hermes bilingual JSON ({"ru":"..."} / {"en":"..."}).</summary>
public sealed class LangUtterance
{
    public LangUtterance(string lang, string text)
    {
        Lang = lang;
        Text = text;
    }

    public string Lang { get; }
    public string Text { get; }
}

public static class BilingualMessageParser
{
    private static readonly Regex JsonObjectRegex = new(
        @"\{[^{}]*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Extract ru/en utterances. Keys are language selectors, not spoken.
    /// Supports one object, many objects in one string, or line-separated JSON.
    /// </summary>
    public static IReadOnlyList<LangUtterance> Parse(string content)
    {
        var list = new List<LangUtterance>();
        var t = (content ?? string.Empty).Trim();
        if (t.Length == 0) return list;

        foreach (Match m in JsonObjectRegex.Matches(t))
        {
            try
            {
                var obj = JObject.Parse(m.Value);
                AddFromObject(obj, list);
            }
            catch
            {
                /* skip non-json chunk */
            }
        }

        if (list.Count == 0 && t.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var obj = JObject.Parse(t);
                AddFromObject(obj, list);
            }
            catch
            {
                /* ignore */
            }
        }

        return list;
    }

    public static bool LooksBilingual(string content)
    {
        var t = (content ?? string.Empty).Trim();
        if (!t.StartsWith("{", StringComparison.Ordinal)) return false;
        return t.IndexOf("\"ru\"", StringComparison.OrdinalIgnoreCase) >= 0
               || t.IndexOf("\"en\"", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string ToDisplayText(IReadOnlyList<LangUtterance> parts)
    {
        if (parts == null || parts.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p.Text)) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(p.Text.Trim());
        }

        return sb.ToString();
    }

    private static void AddFromObject(JObject obj, List<LangUtterance> list)
    {
        foreach (var prop in obj.Properties())
        {
            var name = prop.Name?.Trim() ?? string.Empty;
            if (name.Equals("ru", StringComparison.OrdinalIgnoreCase)
                || name.Equals("en", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ru-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            {
                var text = prop.Value?.Type == JTokenType.String
                    ? prop.Value.ToString()
                    : prop.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    list.Add(new LangUtterance(NormalizeLang(name), text!.Trim()));
            }
        }
    }

    private static string NormalizeLang(string name)
    {
        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        return "ru";
    }
}
