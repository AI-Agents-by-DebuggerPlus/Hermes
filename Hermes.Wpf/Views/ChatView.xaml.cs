using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void ChatView_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        _messagesOwner = vm;
        vm.Chat.Messages.CollectionChanged += ChatMessages_OnCollectionChanged;
    }

    private void ChatView_OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_messagesOwner is not null)
        {
            _messagesOwner.Chat.Messages.CollectionChanged -= ChatMessages_OnCollectionChanged;
            _messagesOwner = null;
        }
    }

    private void ChatMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (MessagesListBox.Items.Count == 0)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            var last = MessagesListBox.Items[^1];
            MessagesListBox.ScrollIntoView(last);
        }, System.Windows.Threading.DispatcherPriority.Background);
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
