using System.Windows;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class RiskManagerViewModel : BaseViewModel
{
    public RiskManagerViewModel(MockTradingDataService data)
    {
        var risk = data.GetRiskStatus();
        RiskLevel = risk.RiskLevel;
        DailyDrawdownPercent = risk.DailyDrawdownPercent;
        ExposurePercent = risk.ExposurePercent;
        CurrentLeverage = risk.Leverage;

        EmergencyStopCommand = new RelayCommand(_ =>
            MessageBox.Show("[Mock] Emergency stop — all strategies halted, reduce-only mode.", "Risk Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    public string RiskLevel { get; }
    public decimal DailyDrawdownPercent { get; }
    public decimal ExposurePercent { get; }
    public decimal CurrentLeverage { get; }

    private decimal _maxDailyLossPercent = 5m;
    private decimal _maxPositionSizeBtc = 0.5m;
    private decimal _maxLeverage = 5m;
    private decimal _maxExposurePercent = 50m;
    private bool _safeMode = true;
    private bool _autoShutdown = true;

    public decimal MaxDailyLossPercent
    {
        get => _maxDailyLossPercent;
        set => SetField(ref _maxDailyLossPercent, value);
    }

    public decimal MaxPositionSizeBtc
    {
        get => _maxPositionSizeBtc;
        set => SetField(ref _maxPositionSizeBtc, value);
    }

    public decimal MaxLeverage
    {
        get => _maxLeverage;
        set => SetField(ref _maxLeverage, value);
    }

    public decimal MaxExposurePercent
    {
        get => _maxExposurePercent;
        set => SetField(ref _maxExposurePercent, value);
    }

    public bool SafeMode
    {
        get => _safeMode;
        set => SetField(ref _safeMode, value);
    }

    public bool AutoShutdown
    {
        get => _autoShutdown;
        set => SetField(ref _autoShutdown, value);
    }

    public RelayCommand EmergencyStopCommand { get; }
}
