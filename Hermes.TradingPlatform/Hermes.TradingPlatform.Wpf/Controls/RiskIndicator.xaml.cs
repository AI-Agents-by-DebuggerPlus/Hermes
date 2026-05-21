using System.Windows;
using System.Windows.Controls;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class RiskIndicator : UserControl
{
    public static readonly DependencyProperty RiskLevelProperty =
        DependencyProperty.Register(nameof(RiskLevel), typeof(string), typeof(RiskIndicator));

    public static readonly DependencyProperty ExposurePercentProperty =
        DependencyProperty.Register(nameof(ExposurePercent), typeof(double), typeof(RiskIndicator));

    public static readonly DependencyProperty DailyDrawdownPercentProperty =
        DependencyProperty.Register(nameof(DailyDrawdownPercent), typeof(double), typeof(RiskIndicator));

    public RiskIndicator() => InitializeComponent();

    public string RiskLevel
    {
        get => (string)GetValue(RiskLevelProperty);
        set => SetValue(RiskLevelProperty, value);
    }

    public double ExposurePercent
    {
        get => (double)GetValue(ExposurePercentProperty);
        set => SetValue(ExposurePercentProperty, value);
    }

    public double DailyDrawdownPercent
    {
        get => (double)GetValue(DailyDrawdownPercentProperty);
        set => SetValue(DailyDrawdownPercentProperty, value);
    }
}
