using System.Windows;
using DesktopVoiceChat.Models;
using DesktopVoiceChat.Services;
using DesktopVoiceChat.ViewModels;

namespace DesktopVoiceChat;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _mainViewModel;

    public SettingsWindow(MainViewModel mainViewModel, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        _mainViewModel = mainViewModel;

        UrlBox.Text = mainViewModel.SupabaseUrl;
        KeyBox.Text = mainViewModel.SupabaseAnonKey;
        SenderBox.Text = mainViewModel.SenderName;
        CheckAnonymous.IsChecked = mainViewModel.UseAnonymousSession;
        CheckOpenAi.IsChecked = mainViewModel.EnableOpenAiReplies;
        OpenAiKeyBox.Text = mainViewModel.OpenAiApiKey;
        OpenAiModelBox.Text = mainViewModel.OpenAiModel;
        OpenAiBotBox.Text = mainViewModel.OpenAiBotSenderName;
        HintPath.Text = $"Файл настроек: {AppSettingsStore.FilePath}";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            SupabaseUrl = UrlBox.Text.Trim(),
            SupabaseAnonKey = KeyBox.Text.Trim(),
            SenderName = string.IsNullOrWhiteSpace(SenderBox.Text)
                ? "WPF User"
                : SenderBox.Text.Trim(),
            UseAnonymousSession = CheckAnonymous.IsChecked != false,
        };

        AppSettingsStore.Save(settings);

        _mainViewModel.SupabaseUrl = settings.SupabaseUrl;
        _mainViewModel.SupabaseAnonKey = settings.SupabaseAnonKey;
        _mainViewModel.SenderName = settings.SenderName;
        _mainViewModel.UseAnonymousSession = settings.UseAnonymousSession;
        _mainViewModel.EnableOpenAiReplies = settings.EnableOpenAiReplies;
        _mainViewModel.OpenAiApiKey = settings.OpenAiApiKey;
        _mainViewModel.OpenAiModel = settings.OpenAiModel;
        _mainViewModel.OpenAiBotSenderName = settings.OpenAiBotSenderName;

        AppLogService.Log(
            $"Настройки сохранены (URL: {settings.SupabaseUrl.Length} симв., ключ: {settings.SupabaseAnonKey.Length} симв., анонимный вход: {settings.UseAnonymousSession}). После смены URL/ключа выполните Connect заново.",
            "Settings");

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
