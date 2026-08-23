using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hermes.Wpf.Services;

/// <summary>
/// Makes http(s)/file URLs and markdown <c>[label](url)</c> clickable inside a chat <see cref="TextBlock"/>.
/// </summary>
public static partial class ChatMessageLinkifier
{
    public static readonly DependencyProperty BindTextProperty = DependencyProperty.RegisterAttached(
        "BindText",
        typeof(string),
        typeof(ChatMessageLinkifier),
        new PropertyMetadata(null, OnBindTextChanged));

    public static void SetBindText(DependencyObject element, string? value) =>
        element.SetValue(BindTextProperty, value);

    public static string? GetBindText(DependencyObject element) =>
        (string?)element.GetValue(BindTextProperty);

    private static void OnBindTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        Apply(block, e.NewValue as string);
    }

    public static void Apply(TextBlock block, string? text)
    {
        block.Inlines.Clear();
        block.Text = null;
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return;
        }

        var fg = block.Foreground ?? Brushes.White;
        var linkBrush = Freeze("#4EA1FF");
        AppendInlines(block.Inlines, raw, fg, linkBrush);
    }

    private static void AppendInlines(InlineCollection inlines, string text, Brush fg, Brush linkBrush)
    {
        var md = MarkdownLinkRegex().Matches(text);
        var bare = BareUrlRegex().Matches(text);
        var spans = new List<(int Start, int End, string Display, string Url)>();

        foreach (Match m in md)
        {
            var url = m.Groups[2].Value.Trim();
            if (!IsNavigable(url))
            {
                continue;
            }

            spans.Add((m.Index, m.Index + m.Length, m.Groups[1].Value.Trim(), url));
        }

        foreach (Match m in bare)
        {
            var url = TrimTrailingPunctuation(m.Value);
            if (!IsNavigable(url))
            {
                continue;
            }

            // Skip if already covered by a markdown link span.
            if (spans.Any(s => m.Index >= s.Start && m.Index < s.End))
            {
                continue;
            }

            spans.Add((m.Index, m.Index + url.Length, url, url));
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start < cursor)
            {
                continue;
            }

            if (span.Start > cursor)
            {
                AppendWithBold(inlines, text[cursor..span.Start], fg);
            }

            inlines.Add(CreateHyperlink(span.Display, span.Url, linkBrush));
            cursor = span.End;
        }

        if (cursor < text.Length)
        {
            AppendWithBold(inlines, text[cursor..], fg);
        }
    }

    private static void AppendWithBold(InlineCollection inlines, string text, Brush fg)
    {
        if (text.Length == 0)
        {
            return;
        }

        var parts = text.Split(["**"], StringSplitOptions.None);
        for (var k = 0; k < parts.Length; k++)
        {
            var chunk = parts[k];
            if (chunk.Length == 0)
            {
                continue;
            }

            if ((k & 1) == 1)
            {
                inlines.Add(new Bold(new Run(chunk) { Foreground = fg }));
            }
            else
            {
                inlines.Add(new Run(chunk) { Foreground = fg });
            }
        }
    }

    private static Hyperlink CreateHyperlink(string display, string url, Brush linkBrush)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Allow Windows paths as file://
            if (url.Length >= 3 && char.IsLetter(url[0]) && url[1] == ':' && (url[2] == '\\' || url[2] == '/'))
            {
                uri = new Uri(url);
            }
            else
            {
                return new Hyperlink(new Run(display)) { Foreground = linkBrush };
            }
        }

        var link = new Hyperlink(new Run(display))
        {
            NavigateUri = uri,
            Foreground = linkBrush,
            TextDecorations = TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = url,
        };
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.IsFile ? e.Uri.LocalPath : e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // ignore open failures
            }

            e.Handled = true;
        };
        return link;
    }

    private static bool IsNavigable(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return url.Length >= 3 && char.IsLetter(url[0]) && url[1] == ':' && (url[2] == '\\' || url[2] == '/');
    }

    private static string TrimTrailingPunctuation(string url)
    {
        var end = url.Length;
        while (end > 0 && ".,;:!?)]}\"'\u00BB".Contains(url[end - 1]))
        {
            end--;
        }

        return end == url.Length ? url : url[..end];
    }

    private static Brush Freeze(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+|file://[^)\s]+|[A-Za-z]:[\\/][^)\s]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://[^\s<>\[\]""']+|file://[^\s<>\[\]""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex();
}
