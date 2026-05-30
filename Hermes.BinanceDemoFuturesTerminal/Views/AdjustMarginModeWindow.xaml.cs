using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.Models;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Views;

public partial class AdjustMarginModeWindow : Window
{
    private readonly AdjustMarginModeViewModel _viewModel;
    private readonly Func<FuturesMarginType, bool, Task<(bool Success, string? Error)>> _applyAsync;

    public AdjustMarginModeWindow(
        AdjustMarginModeViewModel viewModel,
        Func<FuturesMarginType, bool, Task<(bool Success, string? Error)>> applyAsync)
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
            var (success, error) = await _applyAsync(_viewModel.SelectedMarginMode, _viewModel.ApplyToAllSymbols);
            if (!success)
            {
                MessageBox.Show(
                    error ?? "Не удалось изменить режим маржи на demo-fapi.binance.com.",
                    "Режим маржи",
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
