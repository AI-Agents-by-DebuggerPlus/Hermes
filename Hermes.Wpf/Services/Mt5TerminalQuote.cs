using System.Globalization;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>Live quote from HWT status.json (active chart).</summary>
public sealed class Mt5TerminalQuote
{
    public string Symbol { get; init; } = string.Empty;
    public double Bid { get; init; }
    public double Ask { get; init; }
    public int Digits { get; init; } = 2;
    public double Mid => (Bid + Ask) / 2.0;

    public static Mt5TerminalQuote? TryRead(string? projectWindowsPath)
    {
        var raw = Mt5TerminalIpcClient.TryReadStatusJson(projectWindowsPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
            {
                root = snap;
            }

            var symbol = ReadString(root, "symbol") ?? string.Empty;
            var bidText = ReadString(root, "bid");
            var askText = ReadString(root, "ask");
            if (!TryParsePrice(bidText, out var bid) || !TryParsePrice(askText, out var ask))
            {
                return null;
            }

            if (bid <= 0 || ask <= 0)
            {
                return null;
            }

            var digits = Math.Max(CountDecimals(bidText), CountDecimals(askText));
            if (digits <= 0)
            {
                digits = bid >= 100 ? 2 : 5;
            }

            return new Mt5TerminalQuote
            {
                Symbol = symbol.Trim(),
                Bid = bid,
                Ask = ask,
                Digits = digits,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public double Round(double value) =>
        Math.Round(value, Digits, MidpointRounding.AwayFromZero);

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool TryParsePrice(string? text, out double value) =>
        double.TryParse(
            (text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private static int CountDecimals(string? text)
    {
        var t = (text ?? string.Empty).Trim().Replace(',', '.');
        var i = t.IndexOf('.');
        return i < 0 ? 0 : t.Length - i - 1;
    }
}
