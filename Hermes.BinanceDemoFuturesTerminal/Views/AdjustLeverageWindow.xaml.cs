using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Views;

public partial class AdjustLeverageWindow : Window
{
    private readonly AdjustLeverageViewModel _viewModel;
    private readonly Func<int, bool, Task<bool>> _applyAsync;

    public AdjustLeverageWindow(AdjustLeverageViewModel viewModel, Func<int, bool, Task<bool>> applyAsync)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _applyAsync = applyAsync;
        DataContext = viewModel;

        viewModel.ConfirmRequested += OnConfirmRequested;
        viewModel.CancelRequested += OnCancelRequested;
        Closed += (_, _) =>
        {
            viewModel.ConfirmRequested -= OnConfirmRequested;
            viewModel.CancelRequested -= OnCancelRequested;
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCancelRequested(object? sender, EventArgs e) => Close();

    private async void OnConfirmRequested(object? sender, EventArgs e)
    {
        _viewModel.IsBusy = true;
        try
        {
            var ok = await _applyAsync(_viewModel.SelectedLeverage, _viewModel.ApplyToAllSymbols);
            if (!ok)
            {
                MessageBox.Show(
                    "Не удалось установить плечо на demo-fapi.binance.com. Проверьте API-ключи и попробуйте снова.",
                    "Кредитное плечо",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _viewModel.MarkConfirmed();
            DialogResult = true;
            Close();
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
