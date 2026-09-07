using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.RemoteTerminal.Models;

namespace Hermes.RemoteTerminal.Services;

/// <summary>Applies <see cref="UiThemeSettings"/> to the main window chrome and trading panels.</summary>
public static class ThemeApplier
{
    public static void Apply(Window window, AppSettings settings, TradingPanelRefs panels)
    {
        var ui = settings.Ui ?? UiThemeSettings.FromScheme("DarkBinance");
        ui.ClampFonts();
        settings.Ui = ui;

        var bg = UiThemeSettings.BrushFromHex(ui.WindowBackground, "#0B0E11");
        var panel = UiThemeSettings.BrushFromHex(ui.PanelBackground, "#121A28");
        var input = UiThemeSettings.BrushFromHex(ui.InputBackground, "#161B22");
        var text = UiThemeSettings.BrushFromHex(ui.TextPrimary, "#EAECEF");
        var muted = UiThemeSettings.BrushFromHex(ui.TextMuted, "#848E9C");
        var accent = UiThemeSettings.BrushFromHex(ui.TextAccent, "#F0B90B");
        var bid = UiThemeSettings.BrushFromHex(ui.TextBid, "#0ECB81");
        var ask = UiThemeSettings.BrushFromHex(ui.TextAsk, "#F6465D");
        var border = UiThemeSettings.BrushFromHex(ui.Border, "#30363D");

        window.Background = bg;
        window.Foreground = text;

        SetBorder(panels.AccountPanel, panel, border);
        SetBorder(panels.TickerPanel, panel, border);
        SetBorder(panels.PositionsPanel, panel, border);

        SetLabel(panels.AccountTitle, muted, ui.FontLabels);
        SetLabel(panels.TickerTitle, muted, ui.FontLabels);
        SetLabel(panels.PositionsTitle, muted, ui.FontLabels);
        SetLabel(panels.PendingTitle, muted, ui.FontLabels);

        panels.AccountText.Foreground = text;
        panels.AccountText.FontSize = ui.FontAccount;
        panels.AccountText.Background = input;

        panels.SymbolText.Foreground = accent;
        panels.SymbolText.FontSize = ui.FontTicker;

        panels.BidLabel.Foreground = muted;
        panels.BidLabel.FontSize = ui.FontLabels;
        panels.AskLabel.Foreground = muted;
        panels.AskLabel.FontSize = ui.FontLabels;
        panels.LotLabel.Foreground = muted;
        panels.LotLabel.FontSize = ui.FontLabels;

        panels.BidText.Foreground = bid;
        panels.BidText.FontSize = ui.FontPrice;
        panels.AskText.Foreground = ask;
        panels.AskText.FontSize = ui.FontPrice;
        panels.LotText.Foreground = text;
        panels.LotText.FontSize = ui.FontAccount;

        panels.MarketText.Foreground = muted;
        panels.MarketText.FontSize = ui.FontLabels;
        panels.FlagsText.Foreground = muted;
        panels.FlagsText.FontSize = ui.FontLabels;
        panels.SourceText.Foreground = muted;
        panels.SourceText.FontSize = ui.FontLabels;

        panels.PositionsList.Foreground = text;
        panels.PositionsList.FontSize = ui.FontPositions;
        panels.PositionsList.Background = input;
        panels.PendingList.Foreground = text;
        panels.PendingList.FontSize = ui.FontPositions;
        panels.PendingList.Background = input;

        panels.FeedBox.Foreground = text;
        panels.FeedBox.Background = input;
        panels.FeedBox.FontSize = ui.FontFeed;
        panels.StatusText.Foreground = muted;
        panels.HintText.Foreground = muted;
    }

    private static void SetBorder(Border b, Brush fill, Brush border)
    {
        b.Background = fill;
        b.BorderBrush = border;
    }

    private static void SetLabel(TextBlock tb, Brush brush, double size)
    {
        tb.Foreground = brush;
        tb.FontSize = size;
    }
}

public sealed class TradingPanelRefs
{
    public required Border AccountPanel { get; init; }
    public required Border TickerPanel { get; init; }
    public required Border PositionsPanel { get; init; }
    public required TextBlock AccountTitle { get; init; }
    public required TextBlock TickerTitle { get; init; }
    public required TextBlock PositionsTitle { get; init; }
    public required TextBlock PendingTitle { get; init; }
    public required TextBox AccountText { get; init; }
    public required TextBlock SymbolText { get; init; }
    public required TextBlock BidLabel { get; init; }
    public required TextBlock AskLabel { get; init; }
    public required TextBlock LotLabel { get; init; }
    public required TextBlock BidText { get; init; }
    public required TextBlock AskText { get; init; }
    public required TextBlock LotText { get; init; }
    public required TextBlock MarketText { get; init; }
    public required TextBlock FlagsText { get; init; }
    public required TextBlock SourceText { get; init; }
    public required TextBox PositionsList { get; init; }
    public required TextBox PendingList { get; init; }
    public required TextBox FeedBox { get; init; }
    public required TextBlock StatusText { get; init; }
    public required TextBlock HintText { get; init; }
}
