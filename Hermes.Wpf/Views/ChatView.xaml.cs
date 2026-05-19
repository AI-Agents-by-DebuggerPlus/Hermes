using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class ChatView : UserControl
{
    private MainViewModel? _messagesOwner;

    public ChatView()
    {
        InitializeComponent();
        Loaded += ChatView_OnLoaded;
        Unloaded += ChatView_OnUnloaded;
    }

    private void ChatView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        _messagesOwner = vm;
        vm.Chat.Messages.CollectionChanged += ChatMessages_OnCollectionChanged;
        vm.ChatScrollToBottomRequested += OnChatScrollToBottomRequested;
        ScrollToEnd();
    }

    private void ChatView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_messagesOwner is not null)
        {
            _messagesOwner.Chat.Messages.CollectionChanged -= ChatMessages_OnCollectionChanged;
            _messagesOwner.ChatScrollToBottomRequested -= OnChatScrollToBottomRequested;
            _messagesOwner = null;
        }
    }

    private void OnChatScrollToBottomRequested() => ScrollToEnd();

    private void ChatMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Reset)
        {
            ScrollToEnd();
        }
    }

    public void ScrollToEnd()
    {
        if (!IsLoaded)
        {
            Loaded += (_, _) => ScrollToEnd();
            return;
        }

        if (MessagesListBox.Items.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                MessagesListBox.UpdateLayout();
                var last = MessagesListBox.Items[^1];
                MessagesListBox.ScrollIntoView(last);
                FindScrollViewer(MessagesListBox)?.ScrollToEnd();
            },
            DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void ChatImage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.Tag is not string path || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            vm.OpenImageViewer(path);
            e.Handled = true;
        }
    }

    private void MessageInputTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SendMessageCommand.CanExecute(null))
        {
            viewModel.SendMessageCommand.Execute(null);
        }
    }
}
