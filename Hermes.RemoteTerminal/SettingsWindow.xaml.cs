using System.Windows;
using System.Windows.Controls;
using Hermes.RemoteTerminal.Models;

namespace Hermes.RemoteTerminal;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        _loading = true;

        UrlBox.Text = settings.SupabaseUrl;
        KeyBox.Text = settings.SupabaseAnonKey;
        RecipientBox.Text = settings.RecipientName;
        AllRecipientsCheck.IsChecked = settings.ShowAllRecipients;
        MirrorCheck.IsChecked = settings.MirrorLocalLogsToSupabase;
        RestBackupCheck.IsChecked = settings.RestBackupBridgeEnabled;
        RestPollBox.Text = settings.RestBackupPollSeconds.ToString();

        var ui = settings.Ui ?? UiThemeSettings.FromScheme("DarkBinance");
        SelectScheme(ui.ColorScheme);
        FillFonts(ui);
        FillColors(ui);
        _loading = false;
    }

    private void SelectScheme(string scheme)
    {
        for (var i = 0; i < SchemeBox.Items.Count; i++)
        {
            if (SchemeBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Content?.ToString(), scheme, StringComparison.OrdinalIgnoreCase))
            {
                SchemeBox.SelectedIndex = i;
                return;
            }
        }

        SchemeBox.SelectedIndex = 0;
    }

    private void FillFonts(UiThemeSettings ui)
    {
        FontAccountBox.Text = ui.FontAccount.ToString("0");
        FontTickerBox.Text = ui.FontTicker.ToString("0");
        FontPriceBox.Text = ui.FontPrice.ToString("0");
        FontPositionsBox.Text = ui.FontPositions.ToString("0");
        FontLabelsBox.Text = ui.FontLabels.ToString("0");
        FontFeedBox.Text = ui.FontFeed.ToString("0");
    }

    private void FillColors(UiThemeSettings ui)
    {
        WindowBgBox.Text = ui.WindowBackground;
        PanelBgBox.Text = ui.PanelBackground;
        InputBgBox.Text = ui.InputBackground;
        TextPrimaryBox.Text = ui.TextPrimary;
        TextMutedBox.Text = ui.TextMuted;
        TextAccentBox.Text = ui.TextAccent;
        TextBidBox.Text = ui.TextBid;
        TextAskBox.Text = ui.TextAsk;
        BorderBox.Text = ui.Border;
    }

    private void Scheme_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var name = (SchemeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "DarkBinance";
        if (string.Equals(name, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FillColors(UiThemeSettings.FromScheme(name));
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.SupabaseUrl = UrlBox.Text.Trim();
        _settings.SupabaseAnonKey = KeyBox.Text.Trim();
        _settings.RecipientName = string.IsNullOrWhiteSpace(RecipientBox.Text)
            ? "RemoteTerminal"
            : RecipientBox.Text.Trim();
        _settings.ShowAllRecipients = AllRecipientsCheck.IsChecked == true;
        _settings.MirrorLocalLogsToSupabase = MirrorCheck.IsChecked == true;
        _settings.RestBackupBridgeEnabled = RestBackupCheck.IsChecked == true;
        _settings.RestBackupPollSeconds = int.TryParse(RestPollBox.Text.Trim(), out var pollSec) ? pollSec : 15;

        var scheme = (SchemeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "DarkBinance";
        var ui = string.Equals(scheme, "Custom", StringComparison.OrdinalIgnoreCase)
            ? (_settings.Ui ?? new UiThemeSettings { ColorScheme = "Custom" })
            : UiThemeSettings.FromScheme(scheme);

        ui.ColorScheme = scheme;
        ui.FontAccount = ParseFont(FontAccountBox.Text, 15);
        ui.FontTicker = ParseFont(FontTickerBox.Text, 28);
        ui.FontPrice = ParseFont(FontPriceBox.Text, 36);
        ui.FontPositions = ParseFont(FontPositionsBox.Text, 14);
        ui.FontLabels = ParseFont(FontLabelsBox.Text, 12);
        ui.FontFeed = ParseFont(FontFeedBox.Text, 12);

        ui.WindowBackground = HexOr(WindowBgBox.Text, ui.WindowBackground);
        ui.PanelBackground = HexOr(PanelBgBox.Text, ui.PanelBackground);
        ui.InputBackground = HexOr(InputBgBox.Text, ui.InputBackground);
        ui.TextPrimary = HexOr(TextPrimaryBox.Text, ui.TextPrimary);
        ui.TextMuted = HexOr(TextMutedBox.Text, ui.TextMuted);
        ui.TextAccent = HexOr(TextAccentBox.Text, ui.TextAccent);
        ui.TextBid = HexOr(TextBidBox.Text, ui.TextBid);
        ui.TextAsk = HexOr(TextAskBox.Text, ui.TextAsk);
        ui.Border = HexOr(BorderBox.Text, ui.Border);
        ui.ClampFonts();
        _settings.Ui = ui;
        DialogResult = true;
    }

    private static double ParseFont(string text, double fallback) =>
        double.TryParse(text.Trim(), out var v) ? v : fallback;

    private static string HexOr(string text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
}
