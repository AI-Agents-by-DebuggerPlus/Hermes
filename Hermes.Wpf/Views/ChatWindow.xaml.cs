using System.Windows;
using System.Windows.Controls;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;
public partial class ChatWindow : System.Windows.Window
{
    private readonly MainViewModel _viewModel;

    public ChatWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += ChatWindow_OnLoaded;
        ContentRendered += ChatWindow_OnContentRendered;
    }

    private void ChatWindow_OnLoaded(object sender, RoutedEventArgs e) => ScrollChatToEnd();

    private void ChatWindow_OnContentRendered(object? sender, EventArgs e) => ScrollChatToEnd();

    public void ScrollChatToEnd()
    {
        if (Content is not Grid grid || grid.Children.Count == 0)
        {
            return;
        }

        if (grid.Children[0] is ChatView chat)
        {
            chat.ScrollToEnd();
        }

        _viewModel.RequestChatScrollToBottom();
    }
}
