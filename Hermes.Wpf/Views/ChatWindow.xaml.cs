using System.Windows;
using System.Windows.Controls;
using Hermes.Wpf.ViewModels;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;
public partial class ChatWindow : System.Windows.Window
{
    private readonly MainViewModel _viewModel;

    public ChatWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Title = AppVersion.ChatWindowTitle;
        DataContext = viewModel;
        Loaded += ChatWindow_OnLoaded;
        ContentRendered += ChatWindow_OnContentRendered;
    }

    private void ChatWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _ = vm.OnChatWindowOpenedAsync();
        }

        ScrollChatToEnd();
    }

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
