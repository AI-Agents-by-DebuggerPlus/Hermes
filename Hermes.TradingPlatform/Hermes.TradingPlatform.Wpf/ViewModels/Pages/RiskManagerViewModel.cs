using System.Globalization;
using System.Windows;
using Hermes.TradingPlatform.Shared.Risk;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class RiskManagerViewModel : TradingPageViewModel
{
    private readonly TradingPlatformHost _host;
    private bool _suppressPersist;

    public RiskManagerViewModel(TradingReadModel readModel, TradingPlatformHost host)
        : base(readModel)
    {
        _host = host;
        EmergencyStopCommand = new RelayCommand(_ =>
        {
            _host.EmergencyStop("Manual emergency stop from UI.");
            MessageBox.Show(
                "Emergency stop activated. Strategies halted; risk state updated.",
                "Risk Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LoadEditableSettings();
            RefreshMetrics();
        });

        LoadEditableSettings();
        RefreshMetrics();
    }

    private string _riskLevel = "Low";
    private decimal _dailyDrawdownPercent;
    private decimal _exposurePercent;
    private decimal _currentLeverage;
    private bool _emergencyHalt;

    public string RiskLevel
    {
        get => _riskLevel;
        private set => SetField(ref _riskLevel, value);
    }

    public decimal DailyDrawdownPercent
    {
        get => _dailyDrawdownPercent;
        private set => SetField(ref _dailyDrawdownPercent, value);
    }

    public decimal ExposurePercent
    {
        get => _exposurePercent;
        private set => SetField(ref _exposurePercent, value);
    }

    public decimal CurrentLeverage
    {
        get => _currentLeverage;
        private set => SetField(ref _currentLeverage, value);
    }

    public bool EmergencyHalt
    {
        get => _emergencyHalt;
        private set
        {
            if (SetField(ref _emergencyHalt, value))
            {
                Raise(nameof(EmergencyHaltBanner));
            }
        }
    }

    public string EmergencyHaltBanner =>
        EmergencyHalt ? "EMERGENCY HALT ACTIVE" : "";

    private string _maxDailyLossPercentText = "5";
    private string _maxRiskPerTradePercentText = "1";
    private string _maxPositionSizeBtcText = "0.5";
    private string _maxLeverageText = "5";
    private string _maxExposurePercentText = "50";
    private string _defaultTakeProfitRrMultiplierText = "2";
    private bool _autoApplyDefaultSlTp = true;
    private bool _safeMode = true;
    private bool _autoShutdown = true;

    public string MaxDailyLossPercentText
    {
        get => _maxDailyLossPercentText;
        set
        {
            if (SetField(ref _maxDailyLossPercentText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public string MaxRiskPerTradePercentText
    {
        get => _maxRiskPerTradePercentText;
        set
        {
            if (SetField(ref _maxRiskPerTradePercentText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public string MaxPositionSizeBtcText
    {
        get => _maxPositionSizeBtcText;
        set
        {
            if (SetField(ref _maxPositionSizeBtcText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public string MaxLeverageText
    {
        get => _maxLeverageText;
        set
        {
            if (SetField(ref _maxLeverageText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public string MaxExposurePercentText
    {
        get => _maxExposurePercentText;
        set
        {
            if (SetField(ref _maxExposurePercentText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public string DefaultTakeProfitRrMultiplierText
    {
        get => _defaultTakeProfitRrMultiplierText;
        set
        {
            if (SetField(ref _defaultTakeProfitRrMultiplierText, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public bool AutoApplyDefaultSlTp
    {
        get => _autoApplyDefaultSlTp;
        set
        {
            if (SetField(ref _autoApplyDefaultSlTp, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public bool SafeMode
    {
        get => _safeMode;
        set
        {
            if (SetField(ref _safeMode, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public bool AutoShutdown
    {
        get => _autoShutdown;
        set
        {
            if (SetField(ref _autoShutdown, value))
            {
                PersistEditableSettings();
            }
        }
    }

    public RelayCommand EmergencyStopCommand { get; }

    protected override void Refresh()
    {
        RefreshMetrics();
        if (!_suppressPersist)
        {
            LoadEditableSettings();
        }
    }

    private void RefreshMetrics()
    {
        var risk = ReadModel.GetRiskStatus();
        var settings = ReadModel.GetRiskSettings();
        RiskLevel = risk.RiskLevel;
        DailyDrawdownPercent = risk.DailyDrawdownPercent;
        ExposurePercent = risk.ExposurePercent;
        CurrentLeverage = risk.Leverage;
        EmergencyHalt = settings.EmergencyHalt;
    }

    private void LoadEditableSettings()
    {
        var s = ReadModel.GetRiskSettings();
        _suppressPersist = true;
        MaxDailyLossPercentText = s.MaxDailyLossPercent.ToString(CultureInfo.InvariantCulture);
        MaxRiskPerTradePercentText = s.MaxRiskPerTradePercent.ToString(CultureInfo.InvariantCulture);
        MaxPositionSizeBtcText = s.MaxPositionSizeBtc.ToString(CultureInfo.InvariantCulture);
        MaxLeverageText = s.MaxLeverage.ToString(CultureInfo.InvariantCulture);
        MaxExposurePercentText = s.MaxExposurePercent.ToString(CultureInfo.InvariantCulture);
        DefaultTakeProfitRrMultiplierText = s.DefaultTakeProfitRrMultiplier.ToString(CultureInfo.InvariantCulture);
        AutoApplyDefaultSlTp = s.AutoApplyDefaultSlTp;
        SafeMode = s.SafeMode;
        AutoShutdown = s.AutoShutdown;
        EmergencyHalt = s.EmergencyHalt;
        _suppressPersist = false;
    }

    private void PersistEditableSettings()
    {
        if (_suppressPersist)
        {
            return;
        }

        if (!TryBuildSettings(out var settings))
        {
            return;
        }

        _host.PersistRiskSettings(settings);
    }

    private bool TryBuildSettings(out RiskProfileSettingsDto settings)
    {
        settings = new RiskProfileSettingsDto();
        if (!decimal.TryParse(MaxDailyLossPercentText, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxDailyLoss) ||
            !decimal.TryParse(MaxRiskPerTradePercentText, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxRiskTrade) ||
            !decimal.TryParse(MaxPositionSizeBtcText, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxBtc) ||
            !decimal.TryParse(MaxLeverageText, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxLev) ||
            !decimal.TryParse(MaxExposurePercentText, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxExp) ||
            !decimal.TryParse(DefaultTakeProfitRrMultiplierText, NumberStyles.Any, CultureInfo.InvariantCulture, out var tpMult))
        {
            return false;
        }

        settings = new RiskProfileSettingsDto
        {
            MaxDailyLossPercent = maxDailyLoss,
            MaxRiskPerTradePercent = maxRiskTrade,
            MaxPositionSizeBtc = maxBtc,
            MaxLeverage = maxLev,
            MaxExposurePercent = maxExp,
            DefaultTakeProfitRrMultiplier = tpMult > 0 ? tpMult : 2m,
            AutoApplyDefaultSlTp = AutoApplyDefaultSlTp,
            SafeMode = SafeMode,
            AutoShutdown = AutoShutdown,
            EmergencyHalt = EmergencyHalt,
        };
        return true;
    }
}
