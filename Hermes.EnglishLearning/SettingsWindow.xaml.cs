using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.EnglishLearning.Services;

namespace Hermes.EnglishLearning;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<VoiceChoice> _sapiVoices = new();
    private readonly List<AzureVoiceInfo> _azureEn = new();
    private readonly List<AzureVoiceInfo> _azureRu = new();
    private readonly AzureSpeechTtsClient _azureClient = new();
    private readonly SpeechSynthesizer _previewSynth = new();
    private readonly MediaPlayer _previewPlayer = new();
    private CancellationTokenSource? _previewCts;
    private bool _allowVoicePreview;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        Closed += (_, __) => CleanupPreview();
        LoadUi();
        _ = LoadAzureVoicesAsync(forceRefresh: false);
    }

    private void LoadUi()
    {
        _allowVoicePreview = false;
        UrlBox.Text = _settings.SupabaseUrl ?? string.Empty;
        KeyBox.Text = _settings.SupabaseAnonKey ?? string.Empty;
        RecipientBox.Text = string.IsNullOrWhiteSpace(_settings.RecipientName) ? "EnglishLearning" : _settings.RecipientName;
        PollBox.Text = Math.Max(3, _settings.PollSeconds).ToString(CultureInfo.InvariantCulture);
        AutoSpeakBox.IsChecked = _settings.AutoSpeak;

        EnSizeBox.Text = _settings.EnglishFontSize.ToString(CultureInfo.InvariantCulture);
        RuSizeBox.Text = _settings.RussianFontSize.ToString(CultureInfo.InvariantCulture);
        EnColorBox.Text = _settings.EnglishColor;
        RuColorBox.Text = _settings.RussianColor;
        SelectColumns(_settings.WordColumns);

        HotNextBox.Text = _settings.HotkeyNext;
        HotPrevBox.Text = _settings.HotkeyPrev;
        HotSpeakBox.Text = _settings.HotkeySpeak;
        HotFullBox.Text = _settings.HotkeyFullscreen;
        HotStopBox.Text = _settings.HotkeyStop;

        var useAzure = string.Equals(_settings.TtsProvider, "Azure", StringComparison.OrdinalIgnoreCase);
        ProviderAzureRadio.IsChecked = useAzure;
        ProviderSapiRadio.IsChecked = !useAzure;

        _sapiVoices.Clear();
        _sapiVoices.Add(new VoiceChoice("(по культуре ОС)", string.Empty, string.Empty));
        foreach (var v in SettingsStore.ListInstalledVoices())
        {
            _sapiVoices.Add(new VoiceChoice(
                v.Name + " — " + v.Culture.DisplayName,
                v.Name,
                v.Culture.Name));
        }

        EnVoiceBox.ItemsSource = _sapiVoices;
        RuVoiceBox.ItemsSource = _sapiVoices;
        SelectSapiVoice(EnVoiceBox, _settings.EnglishVoiceName);
        SelectSapiVoice(RuVoiceBox, _settings.RussianVoiceName);
        VoiceHintText.Text = BuildSapiHint(_sapiVoices);

        ApplyFallbackAzureLists();
        SelectAzureVoice(AzureEnVoiceBox, _settings.AzureEnglishVoice);
        SelectAzureVoice(AzureRuVoiceBox, _settings.AzureRussianVoice);

        AzureStatusText.Text = AzureSpeechTtsClient.IsConfigured(_settings)
            ? "Ключ Azure найден · смена голоса = тестовая фраза"
            : "Нет ключа Azure в settings.json";

        EnVoiceBox.SelectionChanged += SapiVoice_OnSelectionChanged;
        RuVoiceBox.SelectionChanged += SapiVoice_OnSelectionChanged;
        AzureEnVoiceBox.SelectionChanged += AzureVoice_OnSelectionChanged;
        AzureRuVoiceBox.SelectionChanged += AzureVoice_OnSelectionChanged;
        _allowVoicePreview = true;
    }

    private void SapiVoice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_allowVoicePreview || sender is not ComboBox box || box.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        var english = ReferenceEquals(box, EnVoiceBox);
        PreviewSapi(choice.VoiceName, english);
    }

    private void AzureVoice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_allowVoicePreview || sender is not ComboBox box || box.SelectedItem is not AzureVoiceInfo voice)
        {
            return;
        }

        var english = ReferenceEquals(box, AzureEnVoiceBox);
        _ = PreviewAzureAsync(voice, english);
    }

    private void PreviewSapi(string voiceName, bool english)
    {
        try
        {
            StopPreviewAudio();
            var text = english ? "Hello. This is a voice test." : "Привет. Это проверка голоса.";
            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                _previewSynth.SelectVoice(voiceName);
            }

            _previewSynth.SpeakAsyncCancelAll();
            _previewSynth.SpeakAsync(text);
            AppLog.Info("Voice preview SAPI: " + (string.IsNullOrWhiteSpace(voiceName) ? "(culture)" : voiceName));
        }
        catch (Exception ex)
        {
            AppLog.Warn("SAPI preview failed: " + ex.Message);
        }
    }

    private async Task PreviewAzureAsync(AzureVoiceInfo voice, bool english)
    {
        if (!AzureSpeechTtsClient.IsConfigured(_settings))
        {
            return;
        }

        try
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var ct = _previewCts.Token;
            StopPreviewAudio();

            var text = english ? "Hello. This is a voice test." : "Привет. Это проверка голоса.";
            AzureStatusText.Text = "Превью: " + voice.ShortName + "…";
            var bytes = await _azureClient.SynthesizeAsync(
                _settings, text, voice.ShortName, voice.Locale, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || bytes.Length == 0)
            {
                return;
            }

            var dir = Path.Combine(Path.GetTempPath(), "HermesEnglishLearning");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "voice_preview.mp3");
            File.WriteAllBytes(path, bytes);
            _previewPlayer.Open(new Uri(path, UriKind.Absolute));
            _previewPlayer.Play();
            AzureStatusText.Text = "Превью: " + voice.ShortName;
            AppLog.Info("Voice preview Azure: " + voice.ShortName);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AzureStatusText.Text = "Превью ошибка: " + ex.Message;
            AppLog.Warn("Azure preview failed: " + ex.Message);
        }
    }

    private void StopPreviewAudio()
    {
        try
        {
            _previewSynth.SpeakAsyncCancelAll();
        }
        catch
        {
        }

        try
        {
            _previewPlayer.Stop();
            _previewPlayer.Close();
        }
        catch
        {
        }
    }

    private void CleanupPreview()
    {
        _allowVoicePreview = false;
        try
        {
            _previewCts?.Cancel();
        }
        catch
        {
        }

        StopPreviewAudio();
        try
        {
            _previewSynth.Dispose();
        }
        catch
        {
        }
    }

    private void ApplyFallbackAzureLists()
    {
        var all = AzureSpeechTtsClient.FallbackVoices();
        _azureEn.Clear();
        _azureRu.Clear();
        _azureEn.AddRange(all.Where(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)));
        _azureRu.AddRange(all.Where(v => v.Locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase)));
        AzureEnVoiceBox.ItemsSource = null;
        AzureRuVoiceBox.ItemsSource = null;
        AzureEnVoiceBox.ItemsSource = _azureEn.ToList();
        AzureRuVoiceBox.ItemsSource = _azureRu.ToList();
    }

    private async Task LoadAzureVoicesAsync(bool forceRefresh)
    {
        if (!AzureSpeechTtsClient.IsConfigured(_settings))
        {
            return;
        }

        try
        {
            AzureStatusText.Text = "Загрузка голосов Azure…";
            RefreshAzureVoicesButton.IsEnabled = false;
            var list = await _azureClient.ListVoicesAsync(_settings, default).ConfigureAwait(true);
            _azureEn.Clear();
            _azureRu.Clear();
            _azureEn.AddRange(list.Where(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)));
            _azureRu.AddRange(list.Where(v => v.Locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase)));
            if (_azureEn.Count == 0 || _azureRu.Count == 0)
            {
                ApplyFallbackAzureLists();
                AzureStatusText.Text = "Список пуст — показаны запасные голоса";
            }
            else
            {
                _allowVoicePreview = false;
                AzureEnVoiceBox.ItemsSource = null;
                AzureRuVoiceBox.ItemsSource = null;
                AzureEnVoiceBox.ItemsSource = _azureEn.ToList();
                AzureRuVoiceBox.ItemsSource = _azureRu.ToList();
                SelectAzureVoice(AzureEnVoiceBox, _settings.AzureEnglishVoice);
                SelectAzureVoice(AzureRuVoiceBox, _settings.AzureRussianVoice);
                _allowVoicePreview = true;
                AzureStatusText.Text = "Azure EN=" + _azureEn.Count + ", RU=" + _azureRu.Count
                    + (forceRefresh ? " (обновлено)" : string.Empty)
                    + " · смена голоса = тестовая фраза";
                AppLog.Info("Azure voices loaded: en=" + _azureEn.Count + " ru=" + _azureRu.Count);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Azure voices load failed: " + ex.Message);
            AzureStatusText.Text = "Не удалось загрузить список: " + ex.Message;
        }
        finally
        {
            RefreshAzureVoicesButton.IsEnabled = true;
        }
    }

    private void RefreshAzureVoices_OnClick(object sender, RoutedEventArgs e) =>
        _ = LoadAzureVoicesAsync(forceRefresh: true);

    private void Provider_OnChecked(object sender, RoutedEventArgs e)
    {
        // visual only until Save
    }

    private void SelectColumns(int n)
    {
        foreach (ComboBoxItem item in WordColumnsBox.Items)
        {
            if (item.Tag != null && item.Tag.ToString() == n.ToString(CultureInfo.InvariantCulture))
            {
                WordColumnsBox.SelectedItem = item;
                return;
            }
        }

        WordColumnsBox.SelectedIndex = Math.Max(0, n - 1);
    }

    private static void SelectSapiVoice(ComboBox box, string? name)
    {
        if (box.ItemsSource is not IEnumerable<VoiceChoice> list)
        {
            return;
        }

        var match = list.FirstOrDefault(v =>
            string.Equals(v.VoiceName, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = match ?? list.FirstOrDefault();
    }

    private static void SelectAzureVoice(ComboBox box, string? shortName)
    {
        if (box.ItemsSource is not IEnumerable<AzureVoiceInfo> list)
        {
            return;
        }

        var match = list.FirstOrDefault(v =>
            string.Equals(v.ShortName, shortName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = match ?? list.FirstOrDefault();
    }

    private static string BuildSapiHint(IReadOnlyList<VoiceChoice> voices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Если голос не выбран — берётся голос по культуре en-US / ru-RU.");
        sb.AppendLine("Установлено SAPI голосов: " + Math.Max(0, voices.Count - 1));
        var hasEn = voices.Any(v => v.Culture.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        var hasRu = voices.Any(v => v.Culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        if (!hasEn || !hasRu)
        {
            sb.AppendLine("Не хватает EN или RU SAPI. Для качества используйте Azure Neural + кэш.");
        }

        return sb.ToString().TrimEnd();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.SupabaseUrl = (UrlBox.Text ?? string.Empty).Trim();
        _settings.SupabaseAnonKey = (KeyBox.Text ?? string.Empty).Trim();
        _settings.RecipientName = string.IsNullOrWhiteSpace(RecipientBox.Text) ? "EnglishLearning" : RecipientBox.Text.Trim();
        if (!int.TryParse(PollBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var poll))
        {
            poll = 8;
        }

        _settings.PollSeconds = poll;
        _settings.AutoSpeak = AutoSpeakBox.IsChecked == true;

        if (double.TryParse(EnSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var enSize))
        {
            _settings.EnglishFontSize = enSize;
        }

        if (double.TryParse(RuSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ruSize))
        {
            _settings.RussianFontSize = ruSize;
        }

        _settings.EnglishColor = (EnColorBox.Text ?? string.Empty).Trim();
        _settings.RussianColor = (RuColorBox.Text ?? string.Empty).Trim();

        if (WordColumnsBox.SelectedItem is ComboBoxItem colItem && colItem.Tag != null
            && int.TryParse(colItem.Tag.ToString(), out var cols))
        {
            _settings.WordColumns = cols;
        }

        _settings.TtsProvider = ProviderAzureRadio.IsChecked == true ? "Azure" : "Sapi";

        if (EnVoiceBox.SelectedItem is VoiceChoice enV)
        {
            _settings.EnglishVoiceName = enV.VoiceName;
        }

        if (RuVoiceBox.SelectedItem is VoiceChoice ruV)
        {
            _settings.RussianVoiceName = ruV.VoiceName;
        }

        if (AzureEnVoiceBox.SelectedItem is AzureVoiceInfo azEn)
        {
            _settings.AzureEnglishVoice = azEn.ShortName;
        }

        if (AzureRuVoiceBox.SelectedItem is AzureVoiceInfo azRu)
        {
            _settings.AzureRussianVoice = azRu.ShortName;
        }

        _settings.HotkeyNext = NormalizeKey(HotNextBox.Text, "Right");
        _settings.HotkeyPrev = NormalizeKey(HotPrevBox.Text, "Left");
        _settings.HotkeySpeak = NormalizeKey(HotSpeakBox.Text, "S");
        _settings.HotkeyFullscreen = NormalizeKey(HotFullBox.Text, "F");
        _settings.HotkeyStop = NormalizeKey(HotStopBox.Text, "Escape");

        DialogResult = true;
        Close();
    }

    private static string NormalizeKey(string? text, string fallback)
    {
        var t = (text ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(t) ? fallback : t;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class VoiceChoice
    {
        public VoiceChoice(string display, string voiceName, string culture)
        {
            Display = display;
            VoiceName = voiceName;
            Culture = culture;
        }

        public string Display { get; }
        public string VoiceName { get; }
        public string Culture { get; }
        public string Name => Display;
    }
}
