using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.Wpf.Models;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class ProjectPanel : UserControl
{
    public ProjectPanel()
    {
        InitializeComponent();
    }

    private void ProjectsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectsList.SelectedItem is not HermesProject)
        {
            return;
        }

        if (Window.GetWindow(this) is MainWindow main)
        {
            main.OpenChatWindow();
        }
    }

    private void ProjectsList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            ForceCloseStuckToolTips();
        }

        // Ctrl+↑/↓: always consume — otherwise ListBox moves keyboard focus without
        // changing SelectedItem, and after Ctrl release arrows feel random.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;

            if (DataContext is MainViewModel vm)
            {
                if (e.Key == Key.Up && vm.MoveProjectUpCommand.CanExecute(null))
                {
                    vm.MoveProjectUpCommand.Execute(null);
                }
                else if (e.Key == Key.Down && vm.MoveProjectDownCommand.CanExecute(null))
                {
                    vm.MoveProjectDownCommand.Execute(null);
                }
            }

            Dispatcher.BeginInvoke(SyncListFocusToSelection, DispatcherPriority.Loaded);
            return;
        }

        // Plain arrows: keep focus glued to the selected row after navigation.
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 && e.Key is Key.Up or Key.Down)
        {
            Dispatcher.BeginInvoke(SyncListFocusToSelection, DispatcherPriority.Input);
        }
    }

    private void ProjectsList_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            ForceCloseStuckToolTips();
            SyncListFocusToSelection();
        }
    }

    private void SyncListFocusToSelection()
    {
        try
        {
            var selected = ProjectsList.SelectedItem;
            if (selected is null)
            {
                return;
            }

            ProjectsList.ScrollIntoView(selected);
            ProjectsList.UpdateLayout();

            if (ProjectsList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem row)
            {
                row.IsSelected = true;
                if (!row.IsKeyboardFocusWithin)
                {
                    row.Focus();
                }
            }
            else
            {
                ProjectsList.Focus();
            }
        }
        catch
        {
            // ignore focus chrome failures
        }
    }

    private static void ForceCloseStuckToolTips()
    {
        try
        {
            if (Application.Current is null)
            {
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                CloseToolTipsInTree(window);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void CloseToolTipsInTree(DependencyObject? root)
    {
        if (root is null)
        {
            return;
        }

        switch (root)
        {
            case ToolTip tip:
                tip.IsOpen = false;
                break;
            case Popup popup:
                if (popup.Child is ToolTip popupTip)
                {
                    popupTip.IsOpen = false;
                }

                if (popup.IsOpen && popup.Child is not null && FindToolTip(popup.Child) is { } nested)
                {
                    nested.IsOpen = false;
                    popup.IsOpen = false;
                }

                break;
        }

        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            CloseToolTipsInTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static ToolTip? FindToolTip(DependencyObject? root)
    {
        if (root is ToolTip t)
        {
            return t;
        }

        if (root is null)
        {
            return null;
        }

        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var found = FindToolTip(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
