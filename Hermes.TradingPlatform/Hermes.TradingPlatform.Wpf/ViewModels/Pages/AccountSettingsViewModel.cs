using System.Globalization;
using System.Windows;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class AccountSettingsViewModel : BaseViewModel
{
    private readonly TradingPlatformHost _host;

    public AccountSettingsViewModel(TradingPlatformHost host)
    {
        _host = host;
        var platformSettings = _host.PlatformSettingsStore.Load();
        InitialBalanceText = platformSettings.InitialAccountBalance.ToString(CultureInfo.InvariantCulture);
        AccountLeverageText = platformSettings.AccountLeverage.ToString(CultureInfo.InvariantCulture);
        LeverageModeMaximum = string.Equals(platformSettings.LeverageMode, "Maximum", StringComparison.OrdinalIgnoreCase);
        CurrentBalanceText = _host.ReadModel.GetAccountSummary().Balance.ToString("N2", CultureInfo.InvariantCulture);
        CurrentLeverageText = _host.ReadModel.GetAccountSummary().Leverage.ToString("F1", CultureInfo.InvariantCulture);

        SaveAccountSettingsCommand = new RelayCommand(_ => SaveAccountSettings());
        ResetPaperAccountCommand = new RelayCommand(_ => ResetPaperAccount());
        _host.ReadModel.StateChanged += (_, _) => RefreshLiveAccount();
    }

    private string _statusText = "Paper account. Reset clears positions, orders, and trade journal.";

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string InitialBalanceText { get => _initialBalanceText; set => SetField(ref _initialBalanceText, value); }

    private string _initialBalanceText = "100000";

    public string AccountLeverageText { get => _accountLeverageText; set => SetField(ref _accountLeverageText, value); }

    private string _accountLeverageText = "3";

    public bool LeverageModeMaximum
    {
        get => _leverageModeMaximum;
        set => SetField(ref _leverageModeMaximum, value);
    }

    private bool _leverageModeMaximum;

    public bool LeverageModeFixed
    {
        get => !_leverageModeMaximum;
        set => LeverageModeMaximum = !value;
    }

    public string CurrentBalanceText { get => _currentBalanceText; private set => SetField(ref _currentBalanceText, value); }

    private string _currentBalanceText = "0.00";

    public string CurrentLeverageText { get => _currentLeverageText; private set => SetField(ref _currentLeverageText, value); }

    private string _currentLeverageText = "1";

    public RelayCommand SaveAccountSettingsCommand { get; }
    public RelayCommand ResetPaperAccountCommand { get; }

    private void RefreshLiveAccount()
    {
        var account = _host.ReadModel.GetAccountSummary();
        CurrentBalanceText = account.Balance.ToString("N2", CultureInfo.InvariantCulture);
        CurrentLeverageText = account.Leverage.ToString("F1", CultureInfo.InvariantCulture);
    }

    private void SaveAccountSettings()
    {
        if (!decimal.TryParse(InitialBalanceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var balance)
            || !decimal.TryParse(AccountLeverageText, NumberStyles.Any, CultureInfo.InvariantCulture, out var leverage))
        {
            StatusText = "Invalid balance or leverage format.";
            return;
        }

        var mode = LeverageModeMaximum ? "Maximum" : "Fixed";
        _host.SaveAccountSettings(balance, leverage, mode);
        RefreshLiveAccount();
        StatusText = $"Saved: reset balance {balance:N2}, leverage {leverage:F1}x, mode {(LeverageModeMaximum ? "maximum" : "fixed")}.";
    }

    private void ResetPaperAccount()
    {
        var result = MessageBox.Show(
            "Reset the paper account? All positions, pending orders, and trade history will be removed.",
            "Reset account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _host.ResetPaperAccount();
        RefreshLiveAccount();
        StatusText = "Paper account reset to initial state.";
    }
}
