using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class Mt5TerminalTestOrdersWindow : Window
{
    private readonly Func<string?> _resolveMt5Path;
    private readonly Func<Mt5TerminalRouteCommand, Task<string>> _applyAsync;
    private bool _busy;

    public Mt5TerminalTestOrdersWindow(
        Func<string?> resolveMt5Path,
        Func<Mt5TerminalRouteCommand, Task<string>> applyAsync)
    {
        InitializeComponent();
        _resolveMt5Path = resolveMt5Path ?? throw new ArgumentNullException(nameof(resolveMt5Path));
        _applyAsync = applyAsync ?? throw new ArgumentNullException(nameof(applyAsync));
        Loaded += (_, _) => RefreshCards();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshCards();

    private void RefreshCards()
    {
        var path = _resolveMt5Path();
        var quote = Mt5TerminalQuote.TryRead(path);
        if (quote is null)
        {
            QuoteText.Text = "Нет котировки HWT";
            QuoteHint.Text = "Откройте HermesWpfTerminal (EA) — нужен status.json с bid/ask";
            CardsHost.ItemsSource = null;
            StatusText.Text = "Обновите после старта HWT.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B));
            return;
        }

        QuoteText.Text = $"{quote.Symbol}   bid {Fmt(quote.Bid)}   ask {Fmt(quote.Ask)}   mid {Fmt(quote.Mid)}";
        QuoteHint.Text = $"digits={quote.Digits} · offsets {Mt5TerminalTestOrderCatalog.EntryGapFraction:P2}/{Mt5TerminalTestOrderCatalog.SlFraction:P2}/{Mt5TerminalTestOrderCatalog.TpFraction:P2}";
        CardsHost.ItemsSource = Mt5TerminalTestOrderCatalog.Build(quote);
        StatusText.Text = "Карточки пересчитаны от текущей цены.";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (sender is not Button { Tag: Mt5TerminalTestOrderCard card })
        {
            return;
        }

        _busy = true;
        RefreshButton.IsEnabled = false;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        StatusText.Text = $"Отправка {card.Title}…";
        try
        {
            // Fresh id on each apply.
            card.Command.Id = Guid.NewGuid().ToString("N");
            var result = await _applyAsync(card.Command).ConfigureAwait(true);
            StatusText.Text = result;
            StatusText.Foreground = result.Contains("FAIL", StringComparison.OrdinalIgnoreCase)
                ? Brushes.OrangeRed
                : new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка: " + ex.Message;
            StatusText.Foreground = Brushes.OrangeRed;
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private static string Fmt(double v) => v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
