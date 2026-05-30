using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.MVVM;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels;

public sealed class AdjustMarginModeViewModel : ObservableObject
{
    private FuturesMarginType _selectedMarginMode;
    private bool _isBusy;
    private bool _applyToAllSymbols;

    public AdjustMarginModeViewModel(
        string symbol,
        string contractBadge,
        FuturesMarginType currentMarginMode,
        bool hasOpenPositionOrOrder,
        bool applyToAllSymbolsDefault)
    {
        Symbol = symbol;
        ContractBadge = contractBadge;
        InitialMarginMode = currentMarginMode;
        HasOpenPositionOrOrder = hasOpenPositionOrOrder;
        _selectedMarginMode = currentMarginMode;
        _applyToAllSymbols = applyToAllSymbolsDefault;

        SelectCrossCommand = new RelayCommand(
            _ => SelectedMarginMode = FuturesMarginType.Cross,
            _ => CanSelectMode);
        SelectIsolatedCommand = new RelayCommand(
            _ => SelectedMarginMode = FuturesMarginType.Isolated,
            _ => CanSelectMode);
        ConfirmCommand = new RelayCommand(
            _ => ConfirmRequested?.Invoke(this, EventArgs.Empty),
            _ => CanConfirm);
        CancelCommand = new RelayCommand(
            _ => CancelRequested?.Invoke(this, EventArgs.Empty),
            _ => !IsBusy);
    }

    public string Symbol { get; }
    public string ContractBadge { get; }
    public FuturesMarginType InitialMarginMode { get; }
    public bool HasOpenPositionOrOrder { get; }
    public FuturesMarginType? ConfirmedMarginMode { get; private set; }

    public bool CanSelectMode => !HasOpenPositionOrOrder && !IsBusy;

    public bool CanConfirm =>
        !IsBusy && !HasOpenPositionOrOrder &&
        (SelectedMarginMode != InitialMarginMode || ApplyToAllSymbols);

    public bool ApplyToAllSymbols
    {
        get => _applyToAllSymbols;
        set
        {
            if (!SetProperty(ref _applyToAllSymbols, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ScopeNoticeText));
            NotifySelectionChanged();
        }
    }

    public FuturesMarginType SelectedMarginMode
    {
        get => _selectedMarginMode;
        set
        {
            if (!SetProperty(ref _selectedMarginMode, value))
            {
                return;
            }

            NotifySelectionChanged();
        }
    }

    public bool IsCrossSelected => SelectedMarginMode == FuturesMarginType.Cross;
    public bool IsIsolatedSelected => SelectedMarginMode == FuturesMarginType.Isolated;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifySelectionChanged();
        }
    }

    public string ScopeNoticeText => ApplyToAllSymbols
        ? "• Режим маржи будет установлен по умолчанию для всех контрактов USDT-M Futures Demo."
        : "• Переключение режима маржи применяется только к выбранному контракту на USDT-M Futures Demo.";

    public string CrossExplanationText =>
        "• Кросс-маржа: все кросс-позиции одного маржинального актива (USDT) используют общий баланс. При ликвидации может быть утрачен весь доступный USDT, связанный с этими позициями.";

    public string IsolatedExplanationText =>
        "• Изолированная маржа: маржа по позиции ограничена выделенной суммой. При падении ниже поддерживающей маржи позиция ликвидируется. В этом режиме маржу можно добавлять и снимать.";

    public string RestrictionWarningText =>
        HasOpenPositionOrOrder
            ? "• Режим маржи нельзя изменить, когда по этой паре есть открытая позиция или ордер."
            : string.Empty;

    public ICommand SelectCrossCommand { get; }
    public ICommand SelectIsolatedCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public event EventHandler? ConfirmRequested;
    public event EventHandler? CancelRequested;

    public void MarkConfirmed()
    {
        ConfirmedMarginMode = SelectedMarginMode;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsCrossSelected));
        OnPropertyChanged(nameof(IsIsolatedSelected));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanSelectMode));
        CommandManager.InvalidateRequerySuggested();
    }
}
