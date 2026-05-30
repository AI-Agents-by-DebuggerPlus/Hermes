using System.Globalization;
using System.Windows;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Views;

public partial class PositionSlTpWindow : Window
{
    private readonly PositionModel _position;
    private readonly Func<PositionModel, string, string, Task> _applyAsync;

    public PositionSlTpWindow(PositionModel position, Func<PositionModel, string, string, Task> applyAsync)
    {
        InitializeComponent();
        _position = position;
        _applyAsync = applyAsync;
        TitleText.Text = $"{position.Symbol} · {position.Side} · {position.SizeDisplay}";
        StopLossBox.Text = position.StopLoss?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TakeProfitBox.Text = position.TakeProfit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        try
        {
            await _applyAsync(_position, StopLossBox.Text.Trim(), TakeProfitBox.Text.Trim());
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SL / TP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
