using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class SupabaseTestChatWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly HermesSettings _settings;
    private readonly SupabaseTestChatViewModel _vm;

    public SupabaseTestChatWindow(HermesSettings settings, LogService logService, SettingsService settingsService)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        _vm = new SupabaseTestChatViewModel(settings, logService);
        DataContext = _vm;
        Closing += SupabaseTestChatWindow_OnClosing;
    }

    private async void SupabaseTestChatWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.SyncToSettings();
        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch
        {
            // non-fatal
        }

        _vm.Shutdown();
    }
}
