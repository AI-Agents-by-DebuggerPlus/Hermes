using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class ChatWindow : System.Windows.Window
{
    public ChatWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
