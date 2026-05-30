using System.Windows.Controls;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.ViewModels;

namespace Hermes.BinanceDemoFuturesTerminal.Controls;

public partial class QuantityUnitSelector : UserControl
{
    public QuantityUnitSelector()
    {
        InitializeComponent();
    }

    private void OnSelectContracts(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectContractsModeCommand.Execute(null);
        }
    }
}
