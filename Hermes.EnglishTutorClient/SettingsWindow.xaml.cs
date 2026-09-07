using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hermes.EnglishTutorClient.Services;
using WinForms = System.Windows.Forms;

namespace Hermes.EnglishTutorClient;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SpeechSynthesizer _preview = new();
    private bool _loading;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings ?? new AppSettings();
        LoadUi();
    }

    private void LoadUi()
    {
        _loading = true;
        TutorFontBox.Text = _settings.TutorFontSize.ToString("0");
        UserFontBox.Text = _settings.UserFontSize.ToString("0");
        QuestionFontBox.Text = _settings.QuestionFontSize.ToString("0");
        WordsFontBox.Text = _settings.WordsFontSize.ToString("0");
        TutorColorBox.Text = _settings.TutorColor;
        UserColorBox.Text = _settings.UserColor;
        QuestionColorBox.Text = _settings.QuestionColor;
        WordsColorBox.Text = _settings.WordsColor;

        var useAzure = string.Equals(_settings.TtsProvider, "Azure", StringComparison.OrdinalIgnoreCase);
        AzureRadio.IsChecked = useAzure;
        SapiRadio.IsChecked = !useAzure;

        var voices = SettingsStore.ListInstalledVoices().OrderBy(v => v.Name).ToList();
        EnVoiceBox.ItemsSource = voices;
        RuVoiceBox.ItemsSource = voices;
        SelectSapi(EnVoiceBox, _settings.EnglishVoiceName);
        SelectSapi(RuVoiceBox, _settings.RussianVoiceName);

        AzureKeyBox.Text = _settings.AzureSpeechKey;
        AzureEndpointBox.Text = _settings.AzureSpeechEndpoint;
        AzureRegionBox.Text = _settings.AzureSpeechRegion;
        GoogleSttKeyBox.Text = _settings.GoogleSpeechApiKey;
        VolumeBox.Text = _settings.VolumePercent.ToString();
        AutoSpeakBox.IsChecked = _settings.AutoSpeak;

        PreferRemoteBox.IsChecked = _settings.PreferRemoteWhenConnected;
        SbUrlBox.Text = _settings.SupabaseUrl;
        SbKeyBox.Text = _settings.SupabaseAnonKey;
        RecipientBox.Text = _settings.RecipientName;
        HermesRecipientBox.Text = _settings.HermesRecipientName;
        SenderFilterBox.Text = _settings.SenderNameFilter;
        PollBox.Text = _settings.PollSeconds.ToString();

        HkFullscreen.Text = _settings.HotkeyFullscreen;
        HkSend.Text = _settings.HotkeySend;
        HkNewline.Text = _settings.HotkeyNewline;
        HkSpeak.Text = _settings.HotkeySpeak;
        HkStart.Text = _settings.HotkeyStartTest;
        HkStop.Text = _settings.HotkeyStopTts;
        HkSettings.Text = _settings.HotkeyOpenSettings;
        HkLog.Text = _settings.HotkeyOpenLog;
        HkFocus.Text = _settings.HotkeyFocusInput;
        HkClear.Text = _settings.HotkeyClearChat ?? string.Empty;

        LogFolderBox.Text = _settings.LogFolder;

        var fallback = AzureSpeechTtsClient.FallbackVoices().ToList();
        AzureEnBox.ItemsSource = fallback.Where(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ToList();
        AzureRuBox.ItemsSource = fallback.Where(v => v.Locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).ToList();
        SelectAzure(AzureEnBox, _settings.AzureEnglishVoice);
        SelectAzure(AzureRuBox, _settings.AzureRussianVoice);
        _loading = false;

        if (AzureSpeechTtsClient.IsConfigured(_settings))
            _ = RefreshAzureVoicesAsync();
    }

    private static void SelectSapi(ComboBox box, string name)
    {
        if (box.ItemsSource is not IEnumerable<VoiceInfo> list) return;
        var m = list.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = m ?? list.FirstOrDefault();
    }

    private static void SelectAzure(ComboBox box, string shortName)
    {
        if (box.ItemsSource is not IEnumerable<AzureVoiceInfo> list) return;
        var m = list.FirstOrDefault(v => string.Equals(v.ShortName, shortName, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = m ?? list.FirstOrDefault();
    }

    private async void RefreshAzureVoices_OnClick(object sender, RoutedEventArgs e) =>
        await RefreshAzureVoicesAsync();

    private async Task RefreshAzureVoicesAsync()
    {
        try
        {
            var tmp = SnapshotAzureSettings();
            if (!AzureSpeechTtsClient.IsConfigured(tmp))
            {
                MessageBox.Show("Укажите Azure key и endpoint (или region).", "Голоса",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var client = new AzureSpeechTtsClient();
            var all = await client.ListVoicesAsync(tmp, CancellationToken.None);
            AzureEnBox.ItemsSource = all.Where(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ToList();
            AzureRuBox.ItemsSource = all.Where(v => v.Locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).ToList();
            SelectAzure(AzureEnBox, _settings.AzureEnglishVoice);
            SelectAzure(AzureRuBox, _settings.AzureRussianVoice);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Azure voices: " + ex.Message);
            MessageBox.Show(ex.Message, "Azure voices", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private AppSettings SnapshotAzureSettings()
    {
        return new AppSettings
        {
            AzureSpeechKey = AzureKeyBox.Text.Trim(),
            AzureSpeechEndpoint = AzureEndpointBox.Text.Trim(),
            AzureSpeechRegion = AzureRegionBox.Text.Trim(),
        };
    }

    private void SapiVoice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is not ComboBox { SelectedItem: VoiceInfo v }) return;
        try
        {
            _preview.SpeakAsyncCancelAll();
            _preview.SelectVoice(v.Name);
            _preview.SpeakAsync(v.Culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? "Проверка голоса."
                : "Voice preview.");
        }
        catch { /* ignore */ }
    }

    private void AzureVoice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        // Preview requires network; skip auto-preview to keep settings snappy.
    }

    private void BrowseLogFolder_OnClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Папка для логов English Tutor Client",
            SelectedPath = LogFolderBox.Text,
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            LogFolderBox.Text = dlg.SelectedPath;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(TutorFontBox.Text.Trim(), out var tf)) _settings.TutorFontSize = tf;
        if (double.TryParse(UserFontBox.Text.Trim(), out var uf)) _settings.UserFontSize = uf;
        if (double.TryParse(QuestionFontBox.Text.Trim(), out var qf)) _settings.QuestionFontSize = qf;
        if (double.TryParse(WordsFontBox.Text.Trim(), out var wf)) _settings.WordsFontSize = wf;
        _settings.TutorColor = TutorColorBox.Text.Trim();
        _settings.UserColor = UserColorBox.Text.Trim();
        _settings.QuestionColor = QuestionColorBox.Text.Trim();
        _settings.WordsColor = WordsColorBox.Text.Trim();

        _settings.TtsProvider = AzureRadio.IsChecked == true ? "Azure" : "Sapi";
        if (EnVoiceBox.SelectedItem is VoiceInfo en) _settings.EnglishVoiceName = en.Name;
        if (RuVoiceBox.SelectedItem is VoiceInfo ru) _settings.RussianVoiceName = ru.Name;
        _settings.AzureSpeechKey = AzureKeyBox.Text.Trim();
        _settings.AzureSpeechEndpoint = AzureEndpointBox.Text.Trim();
        _settings.AzureSpeechRegion = AzureRegionBox.Text.Trim();
        _settings.GoogleSpeechApiKey = GoogleSttKeyBox.Text.Trim();
        if (AzureEnBox.SelectedItem is AzureVoiceInfo aen) _settings.AzureEnglishVoice = aen.ShortName;
        if (AzureRuBox.SelectedItem is AzureVoiceInfo aru) _settings.AzureRussianVoice = aru.ShortName;
        if (int.TryParse(VolumeBox.Text.Trim(), out var vol)) _settings.VolumePercent = vol;
        _settings.AutoSpeak = AutoSpeakBox.IsChecked == true;

        _settings.PreferRemoteWhenConnected = PreferRemoteBox.IsChecked == true;
        _settings.SupabaseUrl = SbUrlBox.Text.Trim();
        _settings.SupabaseAnonKey = SbKeyBox.Text.Trim();
        _settings.RecipientName = RecipientBox.Text.Trim();
        _settings.HermesRecipientName = HermesRecipientBox.Text.Trim();
        _settings.SenderNameFilter = SenderFilterBox.Text.Trim();
        if (int.TryParse(PollBox.Text.Trim(), out var poll)) _settings.PollSeconds = poll;

        _settings.HotkeySpeak = HkSpeak.Text.Trim();
        _settings.HotkeyStartTest = HkStart.Text.Trim();
        _settings.HotkeyStopTts = HkStop.Text.Trim();
        _settings.HotkeyOpenSettings = HkSettings.Text.Trim();
        _settings.HotkeyOpenLog = HkLog.Text.Trim();
        _settings.HotkeyFocusInput = HkFocus.Text.Trim();
        _settings.HotkeyClearChat = HkClear.Text.Trim();

        _settings.LogFolder = LogFolderBox.Text.Trim();
        SettingsStore.Save(_settings);
        AppLog.LogFolder = _settings.LogFolder;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _preview.Dispose(); } catch { /* ignore */ }
        base.OnClosed(e);
    }
}
