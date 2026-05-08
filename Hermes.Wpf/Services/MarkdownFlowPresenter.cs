using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hermes.Wpf.Services;

/// <summary>Minimal readable Markdown-ish rendering (# / ## , bullets, bold, fenced code).</summary>
public static class MarkdownFlowPresenter
{
    public static FlowDocument Create(string markdown, Brush foreground, double baseFontSize = 13)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Foreground = foreground,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = baseFontSize,
            Background = Brushes.Transparent,
        };

        var md = (markdown ?? string.Empty).ReplaceLineEndings("\n");
        foreach (var piece in SegmentByFences(md))
        {
            if (piece.Kind == FenceKind.Code)
            {
                var pCode = new Paragraph(new Run(Normalize(piece.Text.TrimEnd())))
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = baseFontSize * 0.93,
                    Background = Freeze("#1a2332"),
                    Margin = new Thickness(8, 4, 8, 6),
                    Padding = new Thickness(6),
                };
                doc.Blocks.Add(pCode);
                continue;
            }

            foreach (var line in piece.Text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 2, 0, 0) });
                    continue;
                }

                var level = HeadingLevel(line);
                if (level > 0)
                {
                    var hdr = HeadingBody(line);
                    var hPar = new Paragraph(new Bold(new Run(Normalize(hdr))))
                    {
                        FontSize = baseFontSize + (level == 1 ? 5 : level == 2 ? 3 : 1),
                    };
                    doc.Blocks.Add(hPar);
                    continue;
                }

                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    trimmed = trimmed[2..].Trim();
                    doc.Blocks.Add(BulletParagraph(trimmed, baseFontSize, foreground));
                    continue;
                }

                doc.Blocks.Add(LineParagraph(trimmed, baseFontSize, foreground));
            }
        }

        return doc;
    }

    private sealed record Segment(FenceKind Kind, string Text);

    private enum FenceKind
    {
        Text,
        Code,
    }

    private static IEnumerable<Segment> SegmentByFences(string md)
    {
        const string delim = "```";
        var idx = md.IndexOf(delim, StringComparison.Ordinal);
        if (idx < 0)
        {
            yield return new Segment(FenceKind.Text, md);
            yield break;
        }

        yield return new Segment(FenceKind.Text, md[..idx]);
        md = md[(idx + delim.Length)..];
        idx = md.IndexOf(delim, StringComparison.Ordinal);
        if (idx < 0)
        {
            yield return new Segment(FenceKind.Text, "```" + md);
            yield break;
        }

        var code = Normalize(md[..idx].TrimStart().TrimEnd());
        md = md[(idx + delim.Length)..];
        yield return new Segment(FenceKind.Code, code);
        foreach (var rest in SegmentByFences(md))
        {
            yield return rest;
        }
    }

    private static int HeadingLevel(string line)
    {
        var s = line.TrimStart();
        var n = 0;
        while (n < s.Length && n < 6 && s[n] == '#')
        {
            n++;
        }

        return n >= 1 && n < s.Length && char.IsWhiteSpace(s[n]) ? n : 0;
    }

    private static string HeadingBody(string line)
    {
        var s = line.TrimStart().AsSpan();
        var i = 0;
        while (i < s.Length && i < 6 && s[i] == '#')
        {
            i++;
        }

        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i >= s.Length ? string.Empty : s[i..].ToString();
    }

    private static Paragraph BulletParagraph(string body, double baseSz, Brush fg)
    {
        var p = new Paragraph { Margin = new Thickness(8, 0, 0, 0), FontSize = baseSz };
        p.Inlines.Add(new Run("• ") { FontWeight = FontWeights.SemiBold });
        AppendWithBoldFragments(p.Inlines, body, fg);
        return p;
    }

    private static Paragraph LineParagraph(string line, double baseSz, Brush fg)
    {
        var p = new Paragraph { Margin = new Thickness(0), FontSize = baseSz };
        AppendWithBoldFragments(p.Inlines, line, fg);
        return p;
    }

    private static void AppendWithBoldFragments(InlineCollection inlines, string line, Brush fg)
    {
        var parts = line.Split(["**"], StringSplitOptions.None);
        for (var k = 0; k < parts.Length; k++)
        {
            var text = Normalize(parts[k]);
            if ((k & 1) == 1)
            {
                inlines.Add(new Bold(new Run(text)) { Foreground = fg });
            }
            else
            {
                inlines.Add(new Run(text) { Foreground = fg });
            }
        }
    }

    private static string Normalize(string s) =>
        s.TrimEnd('\r', '\n').Trim();

    private static Brush Freeze(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
