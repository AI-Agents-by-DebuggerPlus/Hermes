using System.Globalization;
using System.Windows;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Wpf.Views.Dialogs;

public partial class StrategyParametersDialog : Window
{
    public StrategyParametersDialog(string strategyId, string strategyName, StrategyParameters initial)
    {
        InitializeComponent();
        StrategyId = strategyId;
        HeaderText.Text = strategyName;
        SubText.Text = $"Parameters apply on the next tick · saved to disk";

        QuantityBox.Text = initial.Quantity.ToString("0.########", CultureInfo.InvariantCulture);
        ThresholdBox.Text = initial.ChangeThresholdPercent.ToString("0.########", CultureInfo.InvariantCulture);
        CooldownBox.Text = initial.CooldownSeconds.ToString(CultureInfo.InvariantCulture);
    }

    public string StrategyId { get; }
    public StrategyParameters? Result { get; private set; }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty)
            || qty <= 0)
        {
            StatusText.Text = "Quantity must be positive.";
            return;
        }

        if (!decimal.TryParse(ThresholdBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var threshold)
            || threshold <= 0)
        {
            StatusText.Text = "Threshold must be positive.";
            return;
        }

        if (!int.TryParse(CooldownBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var cooldown)
            || cooldown <= 0)
        {
            StatusText.Text = "Cooldown must be positive.";
            return;
        }

        Result = new StrategyParameters
        {
            StrategyId = StrategyId,
            Quantity = qty,
            ChangeThresholdPercent = threshold,
            CooldownSeconds = cooldown,
        };
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
