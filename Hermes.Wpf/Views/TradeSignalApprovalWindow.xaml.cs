using System.Globalization;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class TradeSignalApprovalWindow : Window
{
    public TradeSignalApprovalWindow(TradingAnalyticsSignalCard card, string? chartSymbol)
    {
        InitializeComponent();
        Card = card;
        ChartSymbol = chartSymbol;
        BindFields();
    }

    public TradingAnalyticsSignalCard Card { get; }

    public string? ChartSymbol { get; }

    private void BindFields()
    {
        TitleText.Text = $"{Card.Symbol} · {Card.PendingOrderTypeLabel}";
        SymbolText.Text = Card.Symbol;
        OrderTypeText.Text = Card.PendingOrderTypeLabel;
        SideText.Text = Card.IsBuy ? "Buy" : "Sell";
        EntryText.Text = Card.EntryHigh is > 0 && Card.EntryHigh != Card.Entry
            ? $"{Fmt(Card.Entry)} – {Fmt(Card.EntryHigh.Value)} (→ {Fmt(Card.EffectiveEntry)})"
            : Fmt(Card.EffectiveEntry);
        SlText.Text = Card.StopLoss.HasValue ? Fmt(Card.StopLoss.Value) : "—";
        TpText.Text = Card.TakeProfit.HasValue ? Fmt(Card.TakeProfit.Value) : "—";
        LotText.Text = Card.Lot.HasValue ? Card.Lot.Value.ToString("0.##", CultureInfo.InvariantCulture) : "(default HWT lot)";
        TimeframeText.Text = string.IsNullOrWhiteSpace(Card.Timeframe) ? "—" : Card.Timeframe;
        SummaryText.Text = string.IsNullOrWhiteSpace(Card.Summary) ? "—" : Card.Summary.Trim();
        RefreshWarningsAndButtons();
    }

    private void RefreshWarningsAndButtons()
    {
        var warnings = new List<string>();
        var allowed = Mt5TerminalInstrumentWhitelist.IsAllowed(Card.Symbol);
        if (!allowed)
        {
            warnings.Add("Символ не в whitelist Mt5Terminal.");
        }

        var chartMismatch = !string.IsNullOrWhiteSpace(ChartSymbol)
            && !Mt5TerminalInstrumentWhitelist.MatchesChartSymbol(Card.Symbol, ChartSymbol);
        if (chartMismatch)
        {
            warnings.Add(
                $"График HWT: {ChartSymbol} — ордер будет выставлен на {Card.Symbol} (symbol из сигнала, не графика).");
        }

        if (warnings.Count == 0)
        {
            WarningText.Visibility = Visibility.Collapsed;
        }
        else
        {
            WarningText.Text = string.Join(" ", warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        AddWhitelistBtn.Visibility = allowed ? Visibility.Collapsed : Visibility.Visible;
        ApplyBtn.IsEnabled = allowed;
    }

    private static string Fmt(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private void AddWhitelist_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Mt5TerminalInstrumentWhitelist.TryAddUserSymbol(Card.Symbol, out var normalized))
        {
            MessageBox.Show(this,
                "Не удалось добавить символ.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(Card.Symbol, normalized, StringComparison.OrdinalIgnoreCase))
        {
            SymbolText.Text = normalized;
            TitleText.Text = $"{normalized} · {Card.PendingOrderTypeLabel}";
        }

        RefreshWarningsAndButtons();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Mt5TerminalInstrumentWhitelist.IsAllowed(Card.Symbol))
        {
            MessageBox.Show(this,
                "Символ не в whitelist Mt5Terminal. Нажмите «Add symbol to whitelist» или отмените.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
