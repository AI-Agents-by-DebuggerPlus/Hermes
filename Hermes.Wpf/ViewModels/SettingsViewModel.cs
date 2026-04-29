using Hermes.Wpf.Models;

namespace Hermes.Wpf.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly HermesSettings _settings;
    private string _wslDistro;
    private string _venvPath;
    private string _hermesCommand;
    private int _chatTimeoutSeconds;
    private bool _autoReconnect;

    public SettingsViewModel(HermesSettings settings)
    {
        _settings = settings;
        _wslDistro = settings.WslDistro;
        _venvPath = settings.VenvPath;
        _hermesCommand = settings.HermesCommand;
        _chatTimeoutSeconds = settings.ChatTimeoutSeconds;
        _autoReconnect = settings.AutoReconnect;
    }

    public string WslDistro
    {
        get => _wslDistro;
        set
        {
            _settings.WslDistro = value;
            SetProperty(ref _wslDistro, value);
        }
    }

    public string VenvPath
    {
        get => _venvPath;
        set
        {
            _settings.VenvPath = value;
            SetProperty(ref _venvPath, value);
        }
    }

    public string HermesCommand
    {
        get => _hermesCommand;
        set
        {
            _settings.HermesCommand = value;
            SetProperty(ref _hermesCommand, value);
        }
    }

    public int ChatTimeoutSeconds
    {
        get => _chatTimeoutSeconds;
        set
        {
            _settings.ChatTimeoutSeconds = value;
            SetProperty(ref _chatTimeoutSeconds, value);
        }
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            _settings.AutoReconnect = value;
            SetProperty(ref _autoReconnect, value);
        }
    }
}
