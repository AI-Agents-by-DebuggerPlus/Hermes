using Hermes.InAppAssistant.Wpf;
using Hermes.TradingPlatform.Wpf.Commands;
using Hermes.TradingPlatform.Wpf.Services;

namespace Hermes.TradingPlatform.Wpf.ViewModels.Pages;

public sealed class AssistantViewModel : BaseViewModel
{
    private readonly TradingPlatformHost _host;

    public AssistantViewModel(TradingPlatformHost host, MiniAssistantViewModel inAppAssistant)
    {
        _host = host;
        InAppAssistant = inAppAssistant;
        var platformSettings = _host.PlatformSettingsStore.Load();
        OpenRouterApiKey = platformSettings.InAppAssistantOpenRouterApiKey ?? string.Empty;
        OpenRouterModel = string.IsNullOrWhiteSpace(platformSettings.InAppAssistantOpenRouterModel)
            ? "openrouter/free"
            : platformSettings.InAppAssistantOpenRouterModel;

        SaveOpenRouterCommand = new RelayCommand(_ => SaveOpenRouterSettings());
        StatusText = BuildStatusLine(platformSettings);
    }

    public MiniAssistantViewModel InAppAssistant { get; }

    private string _openRouterApiKey = string.Empty;

    public string OpenRouterApiKey
    {
        get => _openRouterApiKey;
        set
        {
            if (SetField(ref _openRouterApiKey, value ?? string.Empty))
            {
                PreviewSave();
            }
        }
    }

    private string _openRouterModel = "openrouter/free";

    public string OpenRouterModel
    {
        get => _openRouterModel;
        set
        {
            var m = string.IsNullOrWhiteSpace(value) ? "openrouter/free" : value.Trim();
            if (SetField(ref _openRouterModel, m))
            {
                PreviewSave();
            }
        }
    }

    private string _statusText = string.Empty;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public RelayCommand SaveOpenRouterCommand { get; }

    private void PreviewSave()
    {
        if (string.IsNullOrWhiteSpace(OpenRouterApiKey))
        {
            StatusText = "OpenRouter API key is empty. Get a key at openrouter.ai/keys. Free model: openrouter/free.";
            return;
        }

        StatusText =
            $"Ready to save: key {SettingsSaveFeedback.MaskSecret(OpenRouterApiKey)}, model {OpenRouterModel}.";
    }

    private void SaveOpenRouterSettings()
    {
        _host.SetInAppAssistantSettings(OpenRouterApiKey, OpenRouterModel);
        StatusText = SettingsSaveFeedback.OpenRouterSaved(
            OpenRouterApiKey,
            OpenRouterModel,
            _host.PlatformSettingsStore.FilePath);
    }

    private static string BuildStatusLine(Hermes.TradingPlatform.Shared.Settings.PlatformSettingsDto s) =>
        string.IsNullOrWhiteSpace(s.InAppAssistantOpenRouterApiKey)
            ? "Assistant uses OpenRouter (not Gemini). Import a key from Hermes.Wpf or set it here."
            : $"OpenRouter connected: model {s.InAppAssistantOpenRouterModel}, key {SettingsSaveFeedback.MaskSecret(s.InAppAssistantOpenRouterApiKey)}.";
}
