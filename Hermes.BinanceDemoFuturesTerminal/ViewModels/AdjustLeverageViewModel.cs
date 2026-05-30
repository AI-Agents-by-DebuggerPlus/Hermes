using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.MVVM;
using Hermes.BinanceDemoFuturesTerminal.Services;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels;

public sealed class AdjustLeverageViewModel : ObservableObject
{
    private int _selectedLeverage;
    private bool _isBusy;
    private bool _applyToAllSymbols;

    public AdjustLeverageViewModel(
        string symbol,
        int currentLeverage,
        int maxSelectableLeverage,
        int symbolMaxLeverage,
        IReadOnlyList<LeverageBracket> brackets,
        PlatformSettings settings)
    {
        Symbol = symbol;
        SymbolMaxLeverage = symbolMaxLeverage;
        MaxSelectableLeverage = Math.Max(1, maxSelectableLeverage);
        Brackets = brackets;
        Settings = settings;
        InitialLeverage = Math.Clamp(currentLeverage, 1, MaxSelectableLeverage);
        _selectedLeverage = InitialLeverage;
        _applyToAllSymbols = settings.ApplyDefaultLeverageToAllSymbols;

        foreach (var tick in LeverageBracketHelper.GetVisibleTickMarks(MaxSelectableLeverage))
        {
            SliderTickLabels.Add($"{tick}x");
        }

        DecreaseLeverageCommand = new RelayCommand(_ => SelectedLeverage--, _ => SelectedLeverage > 1 && !IsBusy);
        IncreaseLeverageCommand = new RelayCommand(_ => SelectedLeverage++, _ => SelectedLeverage < MaxSelectableLeverage && !IsBusy);
        ConfirmCommand = new RelayCommand(_ => ConfirmRequested?.Invoke(this, EventArgs.Empty), _ => CanConfirm);
        CancelCommand = new RelayCommand(_ => CancelRequested?.Invoke(this, EventArgs.Empty), _ => !IsBusy);
    }

    public string Symbol { get; }
    public int InitialLeverage { get; }
    public int SymbolMaxLeverage { get; }
    public int MaxSelectableLeverage { get; }
    public IReadOnlyList<LeverageBracket> Brackets { get; }
    public PlatformSettings Settings { get; }
    public ObservableCollection<string> SliderTickLabels { get; } = [];

    public int? ConfirmedLeverage { get; private set; }

    public bool CanConfirm =>
        !IsBusy && (SelectedLeverage != InitialLeverage || ApplyToAllSymbols);

    public bool ApplyToAllSymbols
    {
        get => _applyToAllSymbols;
        set
        {
            if (!SetProperty(ref _applyToAllSymbols, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PositionNoticeText));
            OnPropertyChanged(nameof(CanConfirm));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int SelectedLeverage
    {
        get => _selectedLeverage;
        set
        {
            var clamped = Math.Clamp(value, 1, MaxSelectableLeverage);
            if (!SetProperty(ref _selectedLeverage, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedLeverageDisplay));
            OnPropertyChanged(nameof(MaxNotionalText));
            OnPropertyChanged(nameof(RiskWarningText));
            OnPropertyChanged(nameof(CanConfirm));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string SelectedLeverageDisplay => $"{SelectedLeverage}x";

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string MaxNotionalText
    {
        get
        {
            var cap = LeverageBracketHelper.GetMaxNotionalUsdt(Brackets, SelectedLeverage);
            if (cap <= 0)
            {
                return $"• Максимальный номинал при плече {SelectedLeverage}x: данные demo-fapi недоступны.";
            }

            return $"• Максимальный номинал при плече {SelectedLeverage}x: {cap.ToString("N0", CultureInfo.InvariantCulture)} USDT (по таблице demo-fapi.binance.com).";
        }
    }

    public string PositionNoticeText => ApplyToAllSymbols
        ? "• Изменение плеча будет применено ко всем контрактам USDT-M Futures Demo."
        : $"• Изменение плеча применяется к открытой позиции и открытым ордерам по {Symbol} на USDT-M Futures Demo.";

    public string RiskWarningText
    {
        get
        {
            if (Settings.RiskManagementEnabled && Settings.MaxLeverage > 0)
            {
                if (Settings.MaxLeverage < SymbolMaxLeverage)
                {
                    return
                        $"• Риск-менеджер терминала ограничивает «Макс. плечо (проверка)» до {Settings.MaxLeverage}x (макс. на бирже — {SymbolMaxLeverage}x). Ордер будет отклонён, если плечо выше лимита или номинал превышает «Макс. номинал ордера (USDT)» / «Макс. суммарная экспозиция (USDT)».";
                }

                return
                    $"• Риск-менеджер включён: «Макс. плечо (проверка)» — {Settings.MaxLeverage}x, «Макс. номинал ордера (USDT)» — {Settings.MaxOrderUsdt:N0}, «Макс. суммарная экспозиция (USDT)» — {Settings.MaxTotalExposureUsdt:N0}.";
            }

            return
                "• Высокое плечо увеличивает риск ликвидации. Лимиты можно задать в «Настройках» → раздел «Риск-менеджер».";
        }
    }

    public ICommand DecreaseLeverageCommand { get; }
    public ICommand IncreaseLeverageCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public event EventHandler? ConfirmRequested;
    public event EventHandler? CancelRequested;

    public void MarkConfirmed()
    {
        ConfirmedLeverage = SelectedLeverage;
    }
}
