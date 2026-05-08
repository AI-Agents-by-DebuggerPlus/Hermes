using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopVoiceChat.ViewModels;

namespace DesktopVoiceChat;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        ((INotifyCollectionChanged)_viewModel.Messages).CollectionChanged += OnMessagesCollectionChanged;
        MessagesListView.SizeChanged += (_, _) => ScrollMessagesToEnd();
        Loaded += OnMainWindowLoaded;
        Closed += OnMainWindowClosed;
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        ((INotifyCollectionChanged)_viewModel.Messages).CollectionChanged -= OnMessagesCollectionChanged;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Reset:
                ScheduleScrollMessagesToEnd();
                break;
            case NotifyCollectionChangedAction.Move:
                break;
            default:
                ScheduleScrollMessagesToEnd();
                break;
        }
    }

    private void ScheduleScrollMessagesToEnd()
    {
        void Chain()
        {
            ScrollMessagesToEnd();
            Dispatcher.BeginInvoke(ScrollMessagesToEnd, DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(ScrollMessagesToEnd, DispatcherPriority.ContextIdle);
            Dispatcher.BeginInvoke(ScrollMessagesToEnd, DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(ScrollMessagesToEnd, DispatcherPriority.Render);
        }

        Dispatcher.BeginInvoke(Chain, DispatcherPriority.DataBind);
    }

    private void ScrollMessagesToEnd()
    {
        if (MessagesListView.Items.Count == 0)
        {
            return;
        }

        var last = MessagesListView.Items[^1];
        MessagesListView.UpdateLayout();
        MessagesListView.ScrollIntoView(last);

        if (FindDescendant<ScrollViewer>(MessagesListView) is { } sv)
        {
            sv.ScrollToVerticalOffset(Math.Max(0, sv.ExtentHeight - sv.ViewportHeight));
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnMainWindowLoaded;
        await _viewModel.ConnectIfConfiguredAsync();
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.ConnectAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SendAsync();
    }

    private async void OnClearChatClick(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "Удалить все сообщения для всех пользователей из Supabase? Действие необратимо.",
            "Очистка чата",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        await _viewModel.ClearChatAsync();
    }

    private async void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.SendAsync();
    }

    private void OnLogWindowClick(object sender, RoutedEventArgs e)
    {
        LogWindow.ShowOrActivate(this);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel, this);
        dialog.ShowDialog();
    }
}