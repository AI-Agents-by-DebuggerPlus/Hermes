using System.Windows.Media;

namespace Hermes.RemoteTerminal.Models;

/// <summary>Visual theme persisted beside the EXE in settings.json.</summary>
public sealed class UiThemeSettings
{
    /// <summary>Preset name: DarkBinance, DarkBlue, Light, HighContrast, Custom.</summary>
    public string ColorScheme { get; set; } = "DarkBinance";

    public double FontAccount { get; set; } = 15;
    public double FontTicker { get; set; } = 28;
    public double FontPrice { get; set; } = 36;
    public double FontPositions { get; set; } = 14;
    public double FontLabels { get; set; } = 12;
    public double FontFeed { get; set; } = 12;

    public string WindowBackground { get; set; } = "#0B0E11";
    public string PanelBackground { get; set; } = "#121A28";
    public string InputBackground { get; set; } = "#161B22";
    public string TextPrimary { get; set; } = "#EAECEF";
    public string TextMuted { get; set; } = "#848E9C";
    public string TextAccent { get; set; } = "#F0B90B";
    public string TextBid { get; set; } = "#0ECB81";
    public string TextAsk { get; set; } = "#F6465D";
    public string TextPositive { get; set; } = "#0ECB81";
    public string TextNegative { get; set; } = "#F6465D";
    public string Border { get; set; } = "#30363D";

    public static UiThemeSettings FromScheme(string scheme)
    {
        var t = new UiThemeSettings { ColorScheme = scheme };
        switch ((scheme ?? string.Empty).Trim())
        {
            case "DarkBlue":
                t.WindowBackground = "#0A1628";
                t.PanelBackground = "#12253F";
                t.InputBackground = "#0F1C30";
                t.TextPrimary = "#E8EEF7";
                t.TextMuted = "#8FA3BF";
                t.TextAccent = "#4DA3FF";
                t.TextBid = "#3DDC97";
                t.TextAsk = "#FF6B7A";
                t.TextPositive = "#3DDC97";
                t.TextNegative = "#FF6B7A";
                t.Border = "#1E3A5F";
                break;
            case "Light":
                t.WindowBackground = "#F4F6F8";
                t.PanelBackground = "#FFFFFF";
                t.InputBackground = "#EEF1F5";
                t.TextPrimary = "#1A1D21";
                t.TextMuted = "#5C6670";
                t.TextAccent = "#B8860B";
                t.TextBid = "#0A8F5A";
                t.TextAsk = "#C62828";
                t.TextPositive = "#0A8F5A";
                t.TextNegative = "#C62828";
                t.Border = "#C5CCD5";
                break;
            case "HighContrast":
                t.WindowBackground = "#000000";
                t.PanelBackground = "#111111";
                t.InputBackground = "#000000";
                t.TextPrimary = "#FFFFFF";
                t.TextMuted = "#FFFF00";
                t.TextAccent = "#00FFFF";
                t.TextBid = "#00FF00";
                t.TextAsk = "#FF0000";
                t.TextPositive = "#00FF00";
                t.TextNegative = "#FF0000";
                t.Border = "#FFFFFF";
                break;
            case "Custom":
                // keep defaults / caller overrides
                break;
            default: // DarkBinance
                t.ColorScheme = "DarkBinance";
                t.WindowBackground = "#0B0E11";
                t.PanelBackground = "#121A28";
                t.InputBackground = "#161B22";
                t.TextPrimary = "#EAECEF";
                t.TextMuted = "#848E9C";
                t.TextAccent = "#F0B90B";
                t.TextBid = "#0ECB81";
                t.TextAsk = "#F6465D";
                t.TextPositive = "#0ECB81";
                t.TextNegative = "#F6465D";
                t.Border = "#30363D";
                break;
        }

        return t;
    }

    public void ClampFonts()
    {
        FontAccount = Clamp(FontAccount, 10, 48);
        FontTicker = Clamp(FontTicker, 14, 72);
        FontPrice = Clamp(FontPrice, 14, 96);
        FontPositions = Clamp(FontPositions, 10, 40);
        FontLabels = Clamp(FontLabels, 9, 28);
        FontFeed = Clamp(FontFeed, 9, 28);
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : (v > max ? max : v);

    public static Brush BrushFromHex(string? hex, string fallback)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim())!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            var c = (Color)ColorConverter.ConvertFromString(fallback)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
