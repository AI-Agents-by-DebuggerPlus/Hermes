using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.EnglishLearning.Models;
using Hermes.EnglishLearning.Services;
using Microsoft.Win32;

namespace Hermes.EnglishLearning;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LessonTtsService _tts = new();
    private readonly SupabaseRealtimeClient _realtime = new();
    private MediaPlayHotkey? _mediaHotkey;
    private MediaFocusClaimer? _mediaFocus;
    private LogWindow? _logWindow;
    private CancellationTokenSource? _prefetchCts;

    private LessonDocument? _lesson;
    private IReadOnlyList<LessonScreen> _screens = Array.Empty<LessonScreen>();
    private int _index;
    private bool _rebuildQueued;
    private bool _isFullscreen;
    private bool _screenSpeakFinished;
    private WindowState _prevState = WindowState.Normal;
    private WindowStyle _prevStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _prevResize = ResizeMode.CanResize;

    private Brush _enBrush = Brushes.Gold;
    private Brush _ruBrush = Brushes.LightGray;
    private DispatcherTimer? _batteryTimer;
    private string _lastBatteryStatus = string.Empty;
    private DateTime _lastPlayUtc = DateTime.MinValue;
    private bool _volumeUiSync;

    public MainWindow()
    {
        InitializeComponent();
        AppLog.Info("App start");
        _settings = SettingsStore.Load();
        ApplyAppearanceFromSettings();
        _tts.ApplySettings(_settings);
        _tts.SpeakCompleted += (_, __) => Dispatcher.BeginInvoke(new Action(OnSpeakCompleted));
        SyncVolumeUi(_settings.VolumePercent);

        _realtime.LessonReceived += OnLessonReceived;
        _realtime.StatusChanged += s => Dispatcher.BeginInvoke(new Action(() =>
        {
            StatusText.Text = s;
            AppLog.Info(s);
        }));
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        _tts.WarmUp();
        _mediaHotkey = new MediaPlayHotkey(this);
        _mediaHotkey.PlayPausePressed += () => Dispatcher.BeginInvoke(new Action(HandleMediaPlayPause));

        _mediaFocus = new MediaFocusClaimer(this);
        _mediaFocus.PlayPauseFromSystem += () => Dispatcher.BeginInvoke(new Action(HandleMediaPlayPause));
        _mediaFocus.Start();

        Activated += (_, __) => _mediaFocus?.ClaimForeground();

        var sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleLessons", "If_I_Had_a_Heart_lesson.md");
        if (!string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath) && File.Exists(_settings.LastLocalLessonPath))
        {
            LoadLessonFromMarkdown(File.ReadAllText(_settings.LastLocalLessonPath), Path.GetFileName(_settings.LastLocalLessonPath));
        }
        else if (File.Exists(sample))
        {
            LoadLessonFromMarkdown(File.ReadAllText(sample), "If I Had a Heart");
        }

        if (_realtime.IsConfigured(_settings))
        {
            _ = StartRealtimeAsync();
        }

        StatusText.Text = "settings: " + SettingsStore.SettingsPath + " · log: " + AppLog.CurrentLogPath;
        AppLog.Info("UI loaded. Settings=" + SettingsStore.SettingsPath);
        StartBatteryMonitor();
    }

    private void StartBatteryMonitor()
    {
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _batteryTimer.Tick += async (_, __) => await RefreshBatteryAsync().ConfigureAwait(true);
        _batteryTimer.Start();
        _ = RefreshBatteryAsync();
    }

    private async Task RefreshBatteryAsync()
    {
        try
        {
            var devices = await BluetoothHeadsetBatteryReader.ReadAsync().ConfigureAwait(true);
            var endpoint = await DefaultAudioDeviceReader.TryGetDefaultRenderFriendlyNameAsync().ConfigureAwait(true);
            var activeName = DefaultAudioDeviceReader.MatchActiveHeadsetName(endpoint, devices);
            RenderBatteryStatus(devices, activeName);
            var text = BluetoothHeadsetBatteryReader.FormatStatus(devices);
            if (!string.IsNullOrWhiteSpace(activeName))
            {
                text += " | active=" + activeName;
            }

            if (!string.Equals(text, _lastBatteryStatus, StringComparison.Ordinal))
            {
                _lastBatteryStatus = text;
                AppLog.Info("BT battery UI: " + text + (endpoint != null ? " endpoint=[" + endpoint + "]" : string.Empty));
            }
        }
        catch (Exception ex)
        {
            BatteryText.Text = "BT: —";
            AppLog.Warn("BT battery UI: " + ex.Message);
        }
    }

    private void RenderBatteryStatus(IReadOnlyList<HeadsetBatteryInfo> devices, string? activeName)
    {
        BatteryText.Inlines.Clear();
        if (devices == null || devices.Count == 0)
        {
            BatteryText.Text = "BT: —";
            return;
        }

        var muted = (Brush)FindResource("MutedTextBrush");
        var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8D12F"));
        var lowBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0B90B"));

        BatteryText.Inlines.Add(new Run("BT: ") { Foreground = muted });
        for (var i = 0; i < devices.Count; i++)
        {
            if (i > 0)
            {
                BatteryText.Inlines.Add(new Run(" | ") { Foreground = muted });
            }

            var d = devices[i];
            var isActive = !string.IsNullOrWhiteSpace(activeName)
                           && string.Equals(d.Name, activeName, StringComparison.OrdinalIgnoreCase);
            var run = new Run(d.Name + " " + d.Percent + "%");
            if (isActive)
            {
                run.FontWeight = FontWeights.Bold;
                run.Foreground = activeBrush;
            }
            else
            {
                run.FontWeight = FontWeights.Normal;
                run.Foreground = d.Percent <= 20 ? lowBrush : muted;
            }

            BatteryText.Inlines.Add(run);
        }
    }

    private async Task StartRealtimeAsync()
    {
        try
        {
            await _realtime.StartAsync(_settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Realtime start failed", ex);
            StatusText.Text = "Realtime: " + ex.Message;
        }
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lesson == null || _rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildQueued = false;
            RebuildScreens(keepRelativeProgress: true);
        }), DispatcherPriority.Background);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Global RegisterHotKey already handles headset Play — avoid double fire (speak+pause).
        if (e.Key == Key.MediaPlayPause || e.SystemKey == Key.MediaPlayPause)
        {
            if (_mediaHotkey == null || !_mediaHotkey.IsRegistered)
            {
                AppLog.Info("Media Play via PreviewKeyDown (hotkey not registered)");
                HandleMediaPlayPause();
            }
            else
            {
                AppLog.Info("Media Play PreviewKeyDown ignored (handled by global hotkey)");
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            AdjustVolume(+5);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            AdjustVolume(-5);
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _settings.HotkeyFullscreen))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _settings.HotkeyNext) || e.Key == Key.Right || e.Key == Key.PageDown)
        {
            GoNext();
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _settings.HotkeyPrev) || e.Key == Key.Left || e.Key == Key.PageUp || e.Key == Key.Back)
        {
            GoPrev();
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _settings.HotkeySpeak))
        {
            SpeakCurrent();
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _settings.HotkeyStop) || e.Key == Key.Escape)
        {
            if (_isFullscreen && e.Key == Key.Escape)
            {
                ToggleFullscreen();
            }
            else
            {
                _tts.Stop();
                // Interrupted — next Play should speak again, not skip.
                _screenSpeakFinished = false;
            }

            e.Handled = true;
        }
    }

    private void HandleMediaPlayPause()
    {
        var now = DateTime.UtcNow;
        var sinceMs = (now - _lastPlayUtc).TotalMilliseconds;
        // Only suppress true duplicate WM_HOTKEY + SMTC doubles (~same press)
        if (sinceMs < 180)
        {
            AppLog.Info("Media Play ignored (debounce " + (int)sinceMs + "ms)");
            return;
        }

        _lastPlayUtc = now;
        _mediaFocus?.ClaimForeground();

        AppLog.Info("Media Play pressed · speaking=" + _tts.IsSpeaking
            + " paused=" + _tts.IsPaused
            + " active=" + _tts.HasActiveOrPausedUtterance
            + " screenDone=" + _screenSpeakFinished
            + " azure=" + _tts.UsesAzure
            + " screen=" + (_index + 1) + "/" + _screens.Count);

        if (_tts.TryTogglePause())
        {
            StatusText.Text = _tts.IsPaused ? "TTS: пауза" : "TTS: воспроизведение";
            _mediaFocus?.SetTransportStatus(_tts.IsPaused
                ? MediaFocusClaimer.TransportStatus.Paused
                : MediaFocusClaimer.TransportStatus.Playing);
            AppLog.Info("Media Play → pause/resume handled");
            return;
        }

        if (_screenSpeakFinished)
        {
            // One Play after finished: go next AND start speaking (avoids "several presses")
            AppLog.Info("Media Play → next screen + speak");
            if (_index + 1 < _screens.Count)
            {
                GoNext();
                SpeakCurrent();
                StatusText.Text = "Play: следующий экран + озвучка";
            }
            else
            {
                AppLog.Info("Media Play → already last screen");
                StatusText.Text = "TTS: последний экран";
                _mediaFocus?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Stopped);
            }

            return;
        }

        AppLog.Info("Media Play → speak current screen");
        SpeakCurrent();
    }

    private void AdjustVolume(int delta)
    {
        var next = Math.Max(0, Math.Min(100, _settings.VolumePercent + delta));
        ApplyVolume(next, persist: true);
        AppLog.Info("Volume key → " + next + "%");
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeUiSync || !IsLoaded)
        {
            return;
        }

        ApplyVolume((int)Math.Round(e.NewValue), persist: true);
    }

    private void ApplyVolume(int percent, bool persist)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        _settings.VolumePercent = percent;
        _tts.SetVolumePercent(percent);
        SyncVolumeUi(percent);
        if (persist)
        {
            SettingsStore.Save(_settings);
        }

        StatusText.Text = "Громкость озвучки: " + percent + "% (не системный микшер)";
    }

    private void SyncVolumeUi(int percent)
    {
        _volumeUiSync = true;
        try
        {
            if (VolumeSlider != null)
            {
                VolumeSlider.Value = percent;
            }

            if (VolumeValueText != null)
            {
                VolumeValueText.Text = percent.ToString();
            }
        }
        finally
        {
            _volumeUiSync = false;
        }
    }

    private void OnSpeakCompleted()
    {
        _screenSpeakFinished = true;
        StatusText.Text = "TTS: экран завершён — Play = следующий + озвучка";
        _mediaFocus?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Stopped);
        AppLog.Info("TTS screen completed");
    }

    private static bool MatchesHotkey(KeyEventArgs e, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var name = configured!.Trim();
        if (!Enum.TryParse(name, true, out Key key))
        {
            return false;
        }

        return e.Key == key || e.SystemKey == key;
    }

    private void LibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var win = new LessonLibraryWindow { Owner = this };
        if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.SelectedPath) && File.Exists(win.SelectedPath))
        {
            RememberLessonPath(win.SelectedPath!);
            LoadLessonFromMarkdown(File.ReadAllText(win.SelectedPath!), Path.GetFileNameWithoutExtension(win.SelectedPath));
        }
    }

    private void LogButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null)
        {
            _logWindow = new LogWindow { Owner = this };
        }

        _logWindow.ReloadFromAppLog();
        _logWindow.Show();
        _logWindow.Activate();
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            Title = "Открыть урок (MD)",
        };
        if (!string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_settings.LastLocalLessonPath);
        }

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        RememberLessonPath(dlg.FileName);
        LoadLessonFromMarkdown(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() == true)
        {
            SettingsStore.Save(_settings);
            ApplyAppearanceFromSettings();
            _tts.ApplySettings(_settings);
            SyncVolumeUi(_settings.VolumePercent);
            RebuildScreens(keepRelativeProgress: true);
            if (_realtime.IsConfigured(_settings))
            {
                _ = StartRealtimeAsync();
            }
        }
    }

    private void SpeakButton_OnClick(object sender, RoutedEventArgs e) => SpeakCurrent();

    private async void CacheTtsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lesson == null)
        {
            MessageBox.Show(this, "Сначала откройте урок.", "Кэш TTS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AzureSpeechTtsClient.IsConfigured(_settings))
        {
            MessageBox.Show(this, "Нужен Azure ключ в settings.json и провайдер Azure в настройках.", "Кэш TTS",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _prefetchCts?.Cancel();
            _prefetchCts = new CancellationTokenSource();
            var progress = new Progress<string>(s => StatusText.Text = s);
            AppLog.Info("TTS prefetch started for lesson: " + (_lesson.TitleEn ?? "?"));
            await _tts.PrefetchLessonAsync(_lesson, progress, _prefetchCts.Token).ConfigureAwait(true);
            StatusText.Text = "Кэш TTS готов — озвучка будет из tts-cache (см. лог)";
            AppLog.Info("TTS prefetch finished; further Speak uses cache HIT when files exist");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Кэш TTS отменён";
        }
        catch (Exception ex)
        {
            AppLog.Error("TTS prefetch failed", ex);
            MessageBox.Show(this, ex.Message, "Кэш TTS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrevButton_OnClick(object sender, RoutedEventArgs e) => GoPrev();

    private void NextButton_OnClick(object sender, RoutedEventArgs e) => GoNext();

    private void ApplyAppearanceFromSettings()
    {
        _enBrush = ParseBrush(_settings.EnglishColor, "#F8D12F");
        _ruBrush = ParseBrush(_settings.RussianColor, "#D0D4DC");
    }

    private static Brush ParseBrush(string? hex, string fallback)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex);
            return new SolidColorBrush(c);
        }
        catch
        {
            var c = (Color)ColorConverter.ConvertFromString(fallback);
            return new SolidColorBrush(c);
        }
    }

    private void LoadLessonFromMarkdown(string markdown, string titleHint)
    {
        try
        {
            _lesson = LessonMarkdownParser.Parse(markdown);
            if (string.IsNullOrWhiteSpace(_lesson.TitleEn))
            {
                _lesson.TitleEn = titleHint;
            }

            RebuildScreens(keepRelativeProgress: false);
            StatusText.Text = "Урок: " + (_lesson.TitleEn ?? titleHint);
            AppLog.Info("Lesson loaded: " + (_lesson.TitleEn ?? titleHint));
        }
        catch (Exception ex)
        {
            AppLog.Error("Parse lesson failed", ex);
            MessageBox.Show(this, ex.Message, "Ошибка разбора MD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RebuildScreens(bool keepRelativeProgress)
    {
        if (_lesson == null)
        {
            return;
        }

        var ratio = _screens.Count == 0 ? 0.0 : (double)_index / Math.Max(1, _screens.Count);
        var size = new Size(
            Math.Max(200, CardHost.ActualWidth > 0 ? CardHost.ActualWidth : ActualWidth - 40),
            Math.Max(200, CardHost.ActualHeight > 0 ? CardHost.ActualHeight : ActualHeight - 120));

        _screens = LessonPager.BuildScreens(_lesson, size, _settings, CardHost.Padding);

        if (_screens.Count == 0)
        {
            CardsPanel.Children.Clear();
            ProgressText.Text = "Пустой урок";
            return;
        }

        if (keepRelativeProgress)
        {
            _index = Math.Min(_screens.Count - 1, Math.Max(0, (int)Math.Round(ratio * (_screens.Count - 1))));
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath))
        {
            _index = Math.Max(0, Math.Min(_settings.LastScreenIndex, _screens.Count - 1));
        }
        else
        {
            _index = 0;
        }

        ShowCurrentScreen(autoSpeak: !keepRelativeProgress);
    }

    private void ShowCurrentScreen(bool autoSpeak = true)
    {
        if (_screens.Count == 0)
        {
            return;
        }

        _index = Math.Max(0, Math.Min(_index, _screens.Count - 1));
        var screen = _screens[_index];
        ProgressText.Text = LessonPager.FormatProgress(screen, _index, _screens.Count);
        RenderScreen(screen);
        // New screen: Play must speak first; only after SpeakCompleted Play advances.
        _screenSpeakFinished = false;
        PersistScreenProgress();

        if (autoSpeak && _settings.AutoSpeak)
        {
            SpeakCurrent();
        }
    }

    private void RememberLessonPath(string path)
    {
        var changed = !string.Equals(_settings.LastLocalLessonPath, path, StringComparison.OrdinalIgnoreCase);
        _settings.LastLocalLessonPath = path ?? string.Empty;
        if (changed)
        {
            _settings.LastScreenIndex = 0;
        }

        SettingsStore.Save(_settings);
    }

    private void PersistScreenProgress()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath))
        {
            return;
        }

        if (_settings.LastScreenIndex == _index)
        {
            return;
        }

        _settings.LastScreenIndex = _index;
        SettingsStore.Save(_settings);
    }

    private void RenderScreen(LessonScreen screen)
    {
        CardsPanel.Children.Clear();
        CardsPanel.RowDefinitions.Clear();
        CardsPanel.ColumnDefinitions.Clear();

        var hostW = Math.Max(200, CardHost.ActualWidth - CardHost.Padding.Left - CardHost.Padding.Right);
        CardsPanel.Width = hostW;
        CardsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        CardsPanel.VerticalAlignment = VerticalAlignment.Center;

        var cols = Math.Max(1, screen.ColumnCount);
        var cards = screen.Cards;
        var rows = (int)Math.Ceiling(cards.Count / (double)cols);

        for (var c = 0; c < cols; c++)
        {
            CardsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < rows; r++)
        {
            CardsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < cards.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var visual = CreateCardVisual(cards[i], screen.Section);
            Grid.SetColumn(visual, col);
            Grid.SetRow(visual, row);
            visual.Margin = new Thickness(col == 0 ? 0 : 12, row == 0 ? 0 : 16, 0, 0);
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            CardsPanel.Children.Add(visual);
        }
    }

    private FrameworkElement CreateCardVisual(CardPair card, LessonSection section)
    {
        var enSize = section == LessonSection.Words
            ? _settings.EnglishFontSize * 0.88
            : section == LessonSection.Phrases
                ? _settings.EnglishFontSize * 0.95
                : _settings.EnglishFontSize;
        var ruSize = section == LessonSection.Words
            ? _settings.RussianFontSize * 0.88
            : section == LessonSection.Phrases
                ? _settings.RussianFontSize * 0.95
                : _settings.RussianFontSize;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = card.En,
            FontSize = enSize,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = _enBrush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        if (!string.IsNullOrWhiteSpace(card.Ru))
        {
            stack.Children.Add(new TextBlock
            {
                Text = card.Ru,
                FontSize = ruSize,
                FontWeight = FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = _ruBrush,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        return stack;
    }

    private void GoNext()
    {
        if (_index + 1 < _screens.Count)
        {
            _tts.Stop();
            _index++;
            // Don't auto-speak on Play-driven navigation — next Play starts TTS.
            ShowCurrentScreen(autoSpeak: false);
        }
    }

    private void GoPrev()
    {
        if (_index > 0)
        {
            _tts.Stop();
            _index--;
            ShowCurrentScreen(autoSpeak: false);
        }
    }

    private void SpeakCurrent()
    {
        if (_screens.Count == 0)
        {
            return;
        }

        _tts.ApplySettings(_settings);
        _screenSpeakFinished = false;
        _tts.SpeakScreen(_screens[_index]);
        _mediaFocus?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing);
        StatusText.Text = _tts.UsesAzure
            ? "TTS Azure: EN → RU → EN → EN (кэш при наличии)"
            : "TTS SAPI: EN → RU → EN → EN";
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _prevState = WindowState;
            _prevStyle = WindowStyle;
            _prevResize = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            ChromeBar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = _prevStyle;
            ResizeMode = _prevResize;
            WindowState = _prevState;
            ChromeBar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            _isFullscreen = false;
        }

        Dispatcher.BeginInvoke(new Action(() => RebuildScreens(keepRelativeProgress: true)), DispatcherPriority.Loaded);
    }

    private void OnLessonReceived(string markdown, string title)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lessons");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SanitizeFileName(title) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
            File.WriteAllText(path, markdown);
            RememberLessonPath(path);
            AppLog.Info("Auto-open lesson from server: " + path);
            LoadLessonFromMarkdown(markdown, title);
            Activate();
        }));
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "lesson" : name.Trim();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _prefetchCts?.Cancel();
        }
        catch
        {
        }

        _batteryTimer?.Stop();
        PersistScreenProgress();
        SettingsStore.Save(_settings);
        _mediaFocus?.Dispose();
        _mediaHotkey?.Dispose();
        _tts.Dispose();
        _realtime.Dispose();
        _logWindow?.ForceClose();
        // App.OnExit also shuts down AppLog
        base.OnClosed(e);
    }
}
