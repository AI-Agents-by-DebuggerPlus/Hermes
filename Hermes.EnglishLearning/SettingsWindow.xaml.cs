using System;
using System.Windows;
using Hermes.EnglishLearning.Services;

namespace Hermes.EnglishLearning;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        UrlBox.Text = _settings.SupabaseUrl ?? string.Empty;
        KeyBox.Text = _settings.SupabaseAnonKey ?? string.Empty;
        RecipientBox.Text = string.IsNullOrWhiteSpace(_settings.RecipientName) ? "EnglishLearning" : _settings.RecipientName;
        PollBox.Text = Math.Max(3, _settings.PollSeconds).ToString();
        AutoSpeakBox.IsChecked = _settings.AutoSpeak;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.SupabaseUrl = (UrlBox.Text ?? string.Empty).Trim();
        _settings.SupabaseAnonKey = (KeyBox.Text ?? string.Empty).Trim();
        _settings.RecipientName = string.IsNullOrWhiteSpace(RecipientBox.Text) ? "EnglishLearning" : RecipientBox.Text.Trim();
        if (!int.TryParse(PollBox.Text, out var poll))
        {
            poll = 8;
        }

        _settings.PollSeconds = Math.Max(3, Math.Min(120, poll));
        _settings.AutoSpeak = AutoSpeakBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
