using System.Windows;
using System.Windows.Controls;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class WslMemoryView : UserControl
{
    public WslMemoryView()
    {
        InitializeComponent();
    }

    private async void WslMemoryView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WslMemoryViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }
}
