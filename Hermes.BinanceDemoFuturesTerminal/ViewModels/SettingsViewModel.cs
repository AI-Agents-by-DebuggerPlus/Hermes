using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.Helpers;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.MVVM;
using Hermes.BinanceDemoFuturesTerminal.Services;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Action<string, string> _applyCredentials;

    private string _apiKey = string.Empty;
    private string _secretKey = string.Empty;
    private bool _riskEnabled;
    private string _maxOrderMarginPercent = "1";
    private string _maxExposureUsdt = "2000";
    private string _maxOpenPositions = "5";
    private string _maxLeverage = "20";
    private string _defaultAgentOrderUsdt = "50";

    public SettingsViewModel(Action<string, string> applyCredentials)
    {
        _applyCredentials = applyCredentials;
        LoadFrom(AppServices.Settings);
        SaveApiKeysCommand = new RelayCommand(SaveApiKeys);
        SaveRiskCommand = new RelayCommand(SaveRisk);
        ClearKeysCommand = new RelayCommand(ClearKeys);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    public string SecretKey
    {
        get => _secretKey;
        set => SetProperty(ref _secretKey, value);
    }

    public bool RiskManagementEnabled
    {
        get => _riskEnabled;
        set => SetProperty(ref _riskEnabled, value);
    }

    public string MaxOrderMarginPercent
    {
        get => _maxOrderMarginPercent;
        set => SetProperty(ref _maxOrderMarginPercent, value);
    }

    public string MaxTotalExposureUsdt
    {
        get => _maxExposureUsdt;
        set => SetProperty(ref _maxExposureUsdt, value);
    }

    public string MaxOpenPositions
    {
        get => _maxOpenPositions;
        set => SetProperty(ref _maxOpenPositions, value);
    }

    public string MaxLeverage
    {
        get => _maxLeverage;
        set => SetProperty(ref _maxLeverage, value);
    }

    public string DefaultAgentOrderUsdt
    {
        get => _defaultAgentOrderUsdt;
        set => SetProperty(ref _defaultAgentOrderUsdt, value);
    }

    public string SettingsFilePath => TerminalPaths.SettingsFile;

    public ICommand SaveApiKeysCommand { get; }
    public ICommand SaveRiskCommand { get; }
    public ICommand ClearKeysCommand { get; }

    private void LoadFrom(PlatformSettings s)
    {
        ApiKey = s.ApiKey;
        SecretKey = s.SecretKey;
        RiskManagementEnabled = s.RiskManagementEnabled;
        MaxOrderMarginPercent = s.MaxOrderMarginPercent.ToString("0.##", CultureInfo.InvariantCulture);
        MaxTotalExposureUsdt = s.MaxTotalExposureUsdt.ToString("F0");
        MaxOpenPositions = s.MaxOpenPositions.ToString();
        MaxLeverage = s.MaxLeverage.ToString();
        DefaultAgentOrderUsdt = s.DefaultAgentOrderUsdt.ToString("F0");
    }

    private void SaveApiKeys()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(SecretKey))
        {
            MessageBox.Show("Заполните API Key и Secret Key.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = AppServices.Settings;
        settings.ApiKey = ApiKey.Trim();
        settings.SecretKey = SecretKey.Trim();
        AppServices.SaveSettings(settings);
        _applyCredentials(settings.ApiKey, settings.SecretKey);
        AppServices.Log.Info("API-ключи сохранены: " + TerminalPaths.SettingsFile);
        MessageBox.Show("API-ключи сохранены.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveRisk()
    {
        if (!TryParsePositive(MaxOrderMarginPercent, out var maxMarginPercent)
            || maxMarginPercent is <= 0 or > 100
            || !TryParsePositive(MaxTotalExposureUsdt, out var maxExposure)
            || !TryParsePositive(DefaultAgentOrderUsdt, out var defaultAgentUsdt)
            || !int.TryParse(MaxOpenPositions, out var maxPos) || maxPos < 1
            || !int.TryParse(MaxLeverage, out var maxLev) || maxLev < 1)
        {
            MessageBox.Show("Проверьте числовые поля риск-менеджера.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = AppServices.Settings;
        settings.RiskManagementEnabled = RiskManagementEnabled;
        settings.MaxOrderMarginPercent = maxMarginPercent;
        settings.MaxTotalExposureUsdt = maxExposure;
        settings.MaxOpenPositions = maxPos;
        settings.MaxLeverage = maxLev;
        settings.DefaultAgentOrderUsdt = defaultAgentUsdt;
        AppServices.SaveSettings(settings);
        AppServices.Log.Info("Настройки риск-менеджера сохранены.");
        MessageBox.Show("Настройки риск-менеджера сохранены.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearKeys()
    {
        ApiKey = string.Empty;
        SecretKey = string.Empty;
        var settings = AppServices.Settings;
        settings.ApiKey = string.Empty;
        settings.SecretKey = string.Empty;
        AppServices.SaveSettings(settings);
        _applyCredentials(string.Empty, string.Empty);
        AppServices.Log.Warn("API-ключи удалены из настроек.");
    }

    private static bool TryParsePositive(string text, out double value)
    {
        value = 0;
        return double.TryParse(text?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               && value > 0;
    }
}
