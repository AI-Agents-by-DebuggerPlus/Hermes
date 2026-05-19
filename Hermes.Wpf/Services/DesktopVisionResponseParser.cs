namespace Hermes.Wpf.Services;

public static class DesktopVisionResponseParser
{
    private const string CtxBegin = "HERMES_DESKTOP_CTX_BEGIN";
    private const string CtxEnd = "HERMES_DESKTOP_CTX_END";
    private const string UserBegin = "HERMES_DESKTOP_USER_BEGIN";
    private const string UserEnd = "HERMES_DESKTOP_USER_END";

    public static DesktopVisionParsedResponse Parse(string rawText)
    {
        var raw = (rawText ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new DesktopVisionParsedResponse(null, null, raw);
        }

        var ctx = ExtractBlock(raw, CtxBegin, CtxEnd);
        var user = ExtractBlock(raw, UserBegin, UserEnd);

        if (!string.IsNullOrWhiteSpace(ctx) || !string.IsNullOrWhiteSpace(user))
        {
            return new DesktopVisionParsedResponse(ctx, user, raw);
        }

        return new DesktopVisionParsedResponse(raw, null, raw);
    }

    private static string? ExtractBlock(string text, string begin, string end)
    {
        var i = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        var j = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || j < 0 || j <= i)
        {
            return null;
        }

        return text[(i + begin.Length)..j].Trim();
    }
}

public sealed record DesktopVisionParsedResponse(
    string? InternalContext,
    string? UserVisible,
    string Raw);
