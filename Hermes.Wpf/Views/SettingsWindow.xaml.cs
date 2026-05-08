using System.IO;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.ViewModels;
using Microsoft.Win32;

namespace Hermes.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(HermesSettings settings)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(settings);
    }

    private void BrowseExternalBrainMemory_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var dlg = new OpenFolderDialog
        {
            Title = "Папка внешней памяти (Obsidian vault) — искать Markdown рекурсивно",
            InitialDirectory =
                ResolveBrowseHint(vm.ExternalBrainMemoryPath, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        };

        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            return;
        }

        vm.ExternalBrainMemoryPath = dlg.FolderName.Trim();
    }

    private void BrowseWorkspaceRoot_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var dlg = new OpenFolderDialog
        {
            Title = "Основная папка — корень доступа Hermes во все подкаталоги",
            InitialDirectory = ResolveBrowseHint(vm.WorkspaceRootWindowsPath, vm.LastWorkspaceBrowsePath)
        };

        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            return;
        }

        vm.WorkspaceRootWindowsPath = dlg.FolderName.Trim();
        vm.LastWorkspaceBrowsePath = dlg.FolderName.Trim();
    }

    private static string? ResolveBrowseHint(string? primary, string? fallback)
    {
        var p = primary?.Trim();
        if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
        {
            return p;
        }

        var f = fallback?.Trim();
        if (!string.IsNullOrEmpty(f) && Directory.Exists(f))
        {
            return f;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SettingsWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FlushPendingSettingsEdit();
    }

    /// <summary>Apply chat font from text box even if window closes without LostFocus (<see cref="SettingsViewModel.CommitChatFontSize"/>).</summary>
    private void FlushPendingSettingsEdit()
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CommitChatFontSize();
            vm.NormalizeExternalBrainMaxEditField();
        }
    }

    private void ExternalBrainMax_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.NormalizeExternalBrainMaxEditField();
        }
    }

    private void ChatFontSizeTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CommitChatFontSize();
        }
    }
}
