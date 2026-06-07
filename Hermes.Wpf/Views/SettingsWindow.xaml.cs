using System.IO;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;
using Microsoft.Win32;

namespace Hermes.Wpf.Views;

public partial class SettingsWindow : Window
{
    private readonly HermesSettings _settings;
    private readonly LogService? _logService;
    private readonly SettingsService? _settingsService;
    private readonly HermesGalleryPublisher? _galleryPublisher;

    public SettingsWindow(
        HermesSettings settings,
        LogService? logService = null,
        SettingsService? settingsService = null,
        HermesGalleryPublisher? galleryPublisher = null)
    {
        _settings = settings;
        _logService = logService;
        _settingsService = settingsService;
        _galleryPublisher = galleryPublisher;
        InitializeComponent();
        DataContext = new SettingsViewModel(settings, settingsService);
    }

    private void OpenWordPressGallery_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logService is null || _settingsService is null || _galleryPublisher is null)
        {
            MessageBox.Show(
                this,
                "Откройте WordPress с главного окна (кнопка «WordPress»).",
                "WordPress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = new WordPressGalleryWindow(_settings, _logService, _settingsService, _galleryPublisher)
        {
            Owner = this,
        };
        win.Show();
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

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        FlushPendingSettingsEdit();
        if (DataContext is SettingsViewModel vm)
        {
            await vm.SaveToDiskAsync();
        }

        if (Owner is MainWindow { DataContext: MainViewModel mainVm })
        {
            mainVm.ApplyWhatsAppMonitoringSettings();
        }
    }

    private async void SaveOpenRouterSection_OnClick(object sender, RoutedEventArgs e)
    {
        FlushPendingSettingsEdit();
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        if (_settingsService is null)
        {
            vm.RefreshOpenRouterSectionHint(persisted: false);
            vm.SettingsStatusText =
                "Ключ OpenRouter будет записан при закрытии окна или «Сохранить» внизу.";
            return;
        }

        try
        {
            await _settingsService.SaveAsync(_settings);
            vm.RefreshOpenRouterSectionHint(persisted: true);
        }
        catch (Exception ex)
        {
            vm.SettingsStatusText = $"Ошибка сохранения OpenRouter: {ex.Message}";
        }
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
            vm.NormalizeScreenshotMonitorIndexField();
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

    private void ScreenshotMonitorIndex_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.NormalizeScreenshotMonitorIndexField();
        }
    }

    private void BrowseScreenshotDuplicate_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var defaultDup = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "HermesScreenShots");
        var dlg = new OpenFolderDialog
        {
            Title = "Папка для копии скриншотов (дубликат; основной путь не меняется)",
            InitialDirectory = ResolveBrowseHint(vm.DesktopScreenshotDirectory, vm.LastScreenshotBrowsePath)
                                 ?? defaultDup,
        };

        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            return;
        }

        vm.DesktopScreenshotDirectory = dlg.FolderName.Trim();
        vm.LastScreenshotBrowsePath = dlg.FolderName.Trim();
    }
}
