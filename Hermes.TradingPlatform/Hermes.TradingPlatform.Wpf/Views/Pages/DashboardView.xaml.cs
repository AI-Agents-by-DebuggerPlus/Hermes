using System.Windows.Controls;
using Hermes.TradingPlatform.Wpf.ViewModels.Pages;

namespace Hermes.TradingPlatform.Wpf.Views.Pages;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is DashboardViewModel vm)
            {
                PnlWidget.Bind(vm.Pnl);
            }
        };
    }
}
