using System.Windows;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class ExternalBrainWindow : Window
{
    public ExternalBrainWindow(ExternalBrainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, _) => viewModel.DetachFromBrainEvents();
        Owner = Application.Current.MainWindow;
    }
}
