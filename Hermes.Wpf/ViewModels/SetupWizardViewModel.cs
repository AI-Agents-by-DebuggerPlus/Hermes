using System.Windows.Input;
using Hermes.Wpf.Commands;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.ViewModels;

public sealed class SetupWizardViewModel : BaseViewModel
{
    private readonly ConnectionService _connectionService;
    private readonly SettingsService _settingsService;
    private readonly HermesSettings _settings;
    private string _statusText = "Step 1: Run preflight check.";

    public SetupWizardViewModel(ConnectionService connectionService, SettingsService settingsService, HermesSettings settings)
    {
        _connectionService = connectionService;
        _settingsService = settingsService;
        _settings = settings;
        RunPreflightCommand = new RelayCommand(async _ => await RunPreflightAsync());
        InstallHermesCommand = new RelayCommand(async _ => await InstallHermesAsync());
    }

    public ICommand RunPreflightCommand { get; }
    public ICommand InstallHermesCommand { get; }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private async Task RunPreflightAsync()
    {
        StatusText = "Checking environment...";
        var result = await _connectionService.RunPreflightAsync(_settings);

        var lines = new List<string> { result.Message };
        if (result.Diagnostics is { Count: > 0 } dx)
        {
            lines.AddRange(dx.Select(d => $"  [{(d.Ok ? "OK" : "FAIL")}] {d.Label}"));
        }

        if (result.State == ConnectionState.Connected && string.IsNullOrWhiteSpace(_settings.WslDistro))
        {
            var detected = await _connectionService.DetectRecommendedDistroAsync(_settings);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                _settings.WslDistro = detected;
                await _settingsService.SaveAsync(_settings);
                lines.Add($"Persisted WSL distro: {detected}");
            }
        }

        StatusText = string.Join(Environment.NewLine, lines);
    }

    private async Task InstallHermesAsync()
    {
        StatusText = "Installing Hermes...";
        var result = await _connectionService.InstallHermesAsync(_settings);
        StatusText = result.Message;
    }
}
