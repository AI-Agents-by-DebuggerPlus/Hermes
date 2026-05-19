using System.IO;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;
using Microsoft.Win32;

namespace Hermes.Wpf.Views;

public partial class WordPressGalleryWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly HermesSettings _settings;
    private readonly WordPressGalleryViewModel _vm;

    public WordPressGalleryWindow(
        HermesSettings settings,
        LogService logService,
        SettingsService settingsService,
        HermesGalleryPublisher publisher)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        _vm = new WordPressGalleryViewModel(settings, publisher, ShowOpenFileDialog);
        DataContext = _vm;
        Closing += WordPressGalleryWindow_OnClosing;
    }

    private void ShowOpenFileDialog(string initialDirectory)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Изображение для отправки на WordPress",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Все файлы|*.*",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null,
        };

        if (dlg.ShowDialog(this) == true)
        {
            _vm.SetSelectedFile(dlg.FileName);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void WordPressGalleryWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch
        {
            // non-fatal
        }
    }
}
