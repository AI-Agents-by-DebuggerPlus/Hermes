using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class LogsWindow : Window, INotifyPropertyChanged
{
    private readonly HermesSettings _settings;
    private readonly SettingsService? _settingsService;

    public LogsWindow(LogService logService, HermesSettings settings, SettingsService? settingsService = null)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        Entries = logService.Entries;
        Header = $"Log file: {logService.CurrentLogFilePath}";
        DataContext = this;

        Entries.CollectionChanged += (_, _) =>
        {
            if (Entries.Count > 0)
            {
                LogsListBox.ScrollIntoView(Entries[^1]);
            }
        };
    }

    public ObservableCollection<string> Entries { get; }

    public string Header { get; }

    public bool RemoteLogEnabled
    {
        get => _settings.SupabaseRemoteLogEnabled;
        set
        {
            if (_settings.SupabaseRemoteLogEnabled == value)
            {
                return;
            }

            _settings.SupabaseRemoteLogEnabled = value;
            OnPropertyChanged();
            _ = PersistAsync();
        }
    }

    public bool RemoteLogIncludeInfo
    {
        get => _settings.SupabaseRemoteLogIncludeInfo;
        set
        {
            if (_settings.SupabaseRemoteLogIncludeInfo == value)
            {
                return;
            }

            _settings.SupabaseRemoteLogIncludeInfo = value;
            OnPropertyChanged();
            _ = PersistAsync();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async Task PersistAsync()
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch
        {
            // ignore — settings will save on app close
        }
    }
}
