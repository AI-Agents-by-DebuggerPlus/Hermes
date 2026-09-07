using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.EnglishTutorClient.Models;
using Hermes.EnglishTutorClient.Services;

namespace Hermes.EnglishTutorClient;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private readonly DemoSessionRunner _demo = new();
    private readonly TutorTtsService _tts = new();
    private readonly SupabaseTutorClient _supabase = new();
    private readonly VoiceInputService _voice = new();
    private readonly ObservableCollection<ChatBubbleVm> _chat = new();
    private AudioSessionManager? _audioSession;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _batteryTimer;
    private DispatcherTimer? _eqTimer;
    private DispatcherTimer? _silenceAutoSendTimer;
    private readonly Border[] _eqBars = new Border[12];
    private float _eqPeak;
    private DateTime _lastMicActivityUtc = DateTime.MinValue;
    private bool _hadSpeechText;
    private bool _voiceToggleBusy;
    private bool _isFullscreen;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;
    private bool _useRemote;
    private CancellationTokenSource? _cts;
    private string? _activeHeadsetName;
    private string? _lastClaimedHeadset;

    /// <summary>Single SMTC/BT owner — HeadsetTest must use this, not a second MediaFocusClaimer.</summary>
    public AudioSessionManager? AudioSession => _audioSession;

    public MainWindow()
    {
        InitializeComponent();
        ChatList.ItemsSource = _chat;
        BuildEqBars();
    }

    private void BuildEqBars()
    {
        for (var i = 0; i < _eqBars.Length; i++)
        {
            var bar = new Border
            {
                Width = 8,
                Height = 2,
                Margin = new Thickness(1, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B)),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(1),
            };
            _eqBars[i] = bar;
            EqPanel.Children.Add(bar);
        }
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        AppLog.LogFolder = string.IsNullOrWhiteSpace(_settings.LogFolder)
            ? AppLog.ResolveDefaultLogFolder()
            : _settings.LogFolder;
        // Ensure settings.json stores project logs path after migration.
        if (!string.Equals(_settings.LogFolder, AppLog.LogFolder, StringComparison.OrdinalIgnoreCase))
        {
            _settings.LogFolder = AppLog.LogFolder;
            try { SettingsStore.Save(_settings); } catch { /* ignore */ }
        }
        _tts.ApplySettings(_settings);
        _voice.ApplySettings(_settings);
        _demo.LoadDefaultSample();
        _cts = new CancellationTokenSource();

        _audioSession = new AudioSessionManager(this);
        _audioSession.PlayPausePressed += () => Dispatcher.BeginInvoke(new Action(HandleMediaPlayPause));
        _audioSession.Start();
        SpeechRecognizerPicker.LogInstalledRecognizersOnce();

        _voice.PartialResult += text => Dispatcher.BeginInvoke(new Action(() =>
        {
            AnswerBox.Text = text;
            AnswerBox.CaretIndex = AnswerBox.Text.Length;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _hadSpeechText = true;
                _lastMicActivityUtc = DateTime.UtcNow;
                if (_voice.IsListening)
                    StatusText.Text = "🎤 " + text;
            }
        }));
        _voice.SegmentRecognized += text => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _hadSpeechText = true;
            _lastMicActivityUtc = DateTime.UtcNow;
            VoiceLogText.Text = "STT: " + text.Trim();
        }));
        _voice.PeakLevel += peak =>
        {
            _eqPeak = Math.Max(peak, _eqPeak * 0.9f);
            // HFP often kills AVRCP Play — silence after speech auto-sends.
            if (peak >= 0.04f)
                _lastMicActivityUtc = DateTime.UtcNow;
        };
        _voice.StatusChanged += s => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!string.IsNullOrEmpty(s))
                StatusText.Text = s;
        }));

        _eqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _eqTimer.Tick += (_, __) => UpdateEqUi();
        _eqTimer.Start();

        // While listening: after ~3.5s mic quiet + recognized text → stop & send (Play often dead on HFP).
        _silenceAutoSendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _silenceAutoSendTimer.Tick += (_, __) => CheckSilenceAutoSend();

        _supabase.StatusChanged += s => Dispatcher.BeginInvoke(new Action(() => UpdateSupabaseUi(s)));
        _supabase.MessageReceived += (senderName, content) =>
            Dispatcher.BeginInvoke(new Action(() => OnHermesMessage(content)));

        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _batteryTimer.Tick += async (_, __) => await RefreshHeadsetAsync();
        _batteryTimer.Start();
        _ = RefreshHeadsetAsync();

        await ConnectSupabaseAsync();
        AppLog.Info("MainWindow loaded remote=" + _useRemote);
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _pollTimer?.Stop();
        _batteryTimer?.Stop();
        _eqTimer?.Stop();
        try { _silenceAutoSendTimer?.Stop(); } catch { /* ignore */ }
        try { _audioSession?.Dispose(); } catch { /* ignore */ }
        _audioSession = null;
        _voice.Dispose();
        _tts.Dispose();
        _supabase.Dispose();
    }

    private void UpdateEqUi()
    {
        var level = Math.Min(1f, _eqPeak * 1.6f);
        const double maxH = 22;
        for (var i = 0; i < _eqBars.Length; i++)
        {
            var weight = (i + 1) / (double)_eqBars.Length;
            var h = level >= weight ? maxH : Math.Max(2, level / weight * maxH * 0.55);
            if (level < 0.02) h = 2;
            _eqBars[i].Height = h;
        }

        if (!_voice.IsListening)
            _eqPeak *= 0.85f;
        else
            _eqPeak *= 0.92f;
    }

    private async Task ConnectSupabaseAsync()
    {
        var ok = await _supabase.ConnectAsync(_settings, _cts?.Token ?? CancellationToken.None);
        _useRemote = ok && _settings.PreferRemoteWhenConnected;
        ModeText.Text = _useRemote
            ? "Режим: Remote (Hermes)"
            : "Режим: Local demo";
        UpdateSupabaseUi(_supabase.StatusText);
        StartPollTimer();
    }

    private void StartPollTimer()
    {
        _pollTimer?.Stop();
        if (!_supabase.IsConnected) return;
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.PollSeconds))
        };
        _pollTimer.Tick += async (_, __) =>
        {
            try
            {
                await _supabase.PollOnceAsync(_settings, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Poll: " + ex.Message);
            }
        };
        _pollTimer.Start();
    }

    private void UpdateSupabaseUi(string status)
    {
        SupabaseStatusText.Text = status;
        SupabaseDot.Fill = _supabase.IsConnected
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            : new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
    }

    private async Task RefreshHeadsetAsync()
    {
        try
        {
            var devices = await BluetoothHeadsetBatteryReader.ReadAsync();
            var endpoint = await DefaultAudioDeviceReader.TryGetDefaultRenderFriendlyNameAsync();
            var active = DefaultAudioDeviceReader.MatchActiveHeadsetName(endpoint, devices);
            _activeHeadsetName = active;
            if (!string.IsNullOrWhiteSpace(active)
                && !string.Equals(active, _lastClaimedHeadset, StringComparison.OrdinalIgnoreCase))
            {
                _lastClaimedHeadset = active;
                _audioSession?.TryReclaimOnHeadsetAppear(active);
            }

            if (!string.IsNullOrWhiteSpace(active))
            {
                var pct = "";
                foreach (var d in devices)
                {
                    if (string.Equals(d.Name, active, StringComparison.OrdinalIgnoreCase))
                    {
                        pct = " " + d.Percent + "%";
                        break;
                    }
                }

                HeadsetText.Text = "BT: " + active + pct;
                HeadsetText.Foreground = (Brush)FindResource("AccentBrightBrush");
            }
            else
            {
                if (_lastClaimedHeadset != null)
                {
                    _lastClaimedHeadset = null;
                    AppLog.Info("Headset lost — waiting for reconnect to reclaim");
                }

                HeadsetText.Text = BluetoothHeadsetBatteryReader.FormatStatus(devices);
                HeadsetText.Foreground = (Brush)FindResource("MutedBrush");
            }
        }
        catch (Exception ex)
        {
            HeadsetText.Text = "BT: ?";
            AppLog.Warn("Headset: " + ex.Message);
        }
    }

    private void AddBubble(ChatBubbleVm vm)
    {
        ApplyBubbleStyle(vm);
        _chat.Add(vm);
        ChatScroll.ScrollToEnd();
    }

    private void ApplyBubbleStyle(ChatBubbleVm vm)
    {
        switch (vm.Kind)
        {
            case ChatBubbleKind.Tutor:
                vm.FontSize = _settings.TutorFontSize;
                vm.Brush = BrushFrom(_settings.TutorColor, "AccentBrightBrush");
                vm.Align = HorizontalAlignment.Left;
                break;
            case ChatBubbleKind.User:
                vm.FontSize = _settings.UserFontSize;
                vm.Brush = BrushFrom(_settings.UserColor, "TextBrush");
                vm.Align = HorizontalAlignment.Right;
                break;
            default:
                vm.FontSize = 13;
                vm.Brush = (Brush)FindResource("MutedBrush");
                vm.Align = HorizontalAlignment.Center;
                break;
        }

        vm.QuestionFontSize = _settings.QuestionFontSize;
        vm.WordsFontSize = _settings.WordsFontSize;
        vm.QuestionBrush = BrushFrom(_settings.QuestionColor, "MutedBrush");
        vm.WordsBrush = BrushFrom(_settings.WordsColor, "AccentBrightBrush");
    }

    private Brush BrushFrom(string hex, string fallbackKey)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return (Brush)FindResource(fallbackKey); }
    }

    private void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_useRemote)
        {
            _ = SendToHermesAsync("Начни первичный тест уровня английского языка.");
            return;
        }

        _demo.LoadDefaultSample();
        var ex = _demo.Start();
        ShowLocalExercise(ex);
    }

    private void ShowLocalExercise(TutorExercise ex)
    {
        var words = ex.Words != null && ex.Words.Count > 0
            ? string.Join("   ·   ", ex.Words)
            : null;
        AddBubble(new ChatBubbleVm
        {
            Kind = ChatBubbleKind.Tutor,
            Question = ex.Question,
            Words = words,
            Text = string.Empty,
        });
        if (_settings.AutoSpeak)
        {
            var joined = ex.Words != null ? string.Join(" ", ex.Words) : string.Empty;
            _tts.SpeakExercise(ex.Question, ex.QuestionLang, joined, ex.WordsLang);
        }
    }

    private void OnHermesMessage(string content)
    {
        if (TutorWireMessage.TryParse(content, out var wire) && wire != null)
        {
            var words = wire.Words != null && wire.Words.Count > 0
                ? string.Join("   ·   ", wire.Words)
                : null;
            var text = wire.Text ?? wire.Feedback ?? string.Empty;
            AddBubble(new ChatBubbleVm
            {
                Kind = ChatBubbleKind.Tutor,
                Question = wire.Question,
                Words = words,
                Text = text,
            });

            var speak = wire.Speak != false && _settings.AutoSpeak;
            if (speak)
            {
                if (!string.IsNullOrWhiteSpace(wire.Question) || !string.IsNullOrWhiteSpace(words))
                {
                    var joined = wire.Words != null ? string.Join(" ", wire.Words) : string.Empty;
                    _tts.SpeakExercise(
                        wire.Question ?? string.Empty,
                        wire.QuestionLang ?? "ru",
                        joined,
                        wire.WordsLang ?? "en");
                }
                else if (!string.IsNullOrWhiteSpace(text))
                {
                    _tts.Speak(text, "ru");
                }
            }

            return;
        }

        // {"ru":"..."} / {"en":"..."} — key selects voice, key itself is not spoken.
        if (BilingualMessageParser.LooksBilingual(content))
        {
            var parts = BilingualMessageParser.Parse(content);
            var display = BilingualMessageParser.ToDisplayText(parts);
            if (string.IsNullOrWhiteSpace(display))
                display = content;
            AddBubble(new ChatBubbleVm { Kind = ChatBubbleKind.Tutor, Text = display });
            if (_settings.AutoSpeak && parts.Count > 0)
                _tts.SpeakSequence(parts);
            else if (_settings.AutoSpeak && !string.IsNullOrWhiteSpace(display))
                _tts.Speak(display, "ru");
            return;
        }

        var plain = (content ?? string.Empty).Trim();
        AddBubble(new ChatBubbleVm { Kind = ChatBubbleKind.Tutor, Text = plain });
        if (_settings.AutoSpeak)
            _tts.Speak(plain, "ru");
    }

    private void SendButton_OnClick(object sender, RoutedEventArgs e) => _ = SendCurrentAsync();

    private async Task SendCurrentAsync()
    {
        var text = (AnswerBox.Text ?? string.Empty).Trim();
        if (text.Length == 0) return;
        AnswerBox.Text = string.Empty;
        AddBubble(new ChatBubbleVm { Kind = ChatBubbleKind.User, Text = text });

        if (_useRemote)
        {
            await SendToHermesAsync(text);
            return;
        }

        // Local demo check
        var fb = _demo.Check(text);
        AddBubble(new ChatBubbleVm
        {
            Kind = ChatBubbleKind.Tutor,
            Text = fb.Message,
        });
        if (_settings.AutoSpeak)
            _tts.Speak(fb.Message, "ru");

        if (fb.IsCorrect)
        {
            var next = _demo.Next();
            if (next != null)
                ShowLocalExercise(next);
            else
                AddBubble(new ChatBubbleVm { Kind = ChatBubbleKind.System, Text = "Тест завершён (local)." });
        }
    }

    private async Task SendToHermesAsync(string text)
    {
        try
        {
            StatusText.Text = "Отправка…";
            await _supabase.SendAsync(_settings, text, _cts?.Token ?? CancellationToken.None);
            StatusText.Text = "Отправлено Hermes";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка отправки";
            AppLog.Error("Send: " + ex.Message);
            AddBubble(new ChatBubbleVm
            {
                Kind = ChatBubbleKind.System,
                Text = "Не удалось отправить в Supabase: " + ex.Message,
            });
        }
    }

    private void AnswerBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return; // newline
        e.Handled = true;
        _ = SendCurrentAsync();
    }

    private void SpeakButton_OnClick(object sender, RoutedEventArgs e)
    {
        for (var i = _chat.Count - 1; i >= 0; i--)
        {
            var b = _chat[i];
            if (b.Kind != ChatBubbleKind.Tutor) continue;
            if (!string.IsNullOrWhiteSpace(b.Question) || !string.IsNullOrWhiteSpace(b.Words))
            {
                _tts.SpeakExercise(b.Question ?? "", "ru", (b.Words ?? "").Replace("·", " "), "en");
                return;
            }
            if (!string.IsNullOrWhiteSpace(b.Text))
            {
                _tts.Speak(b.Text, "ru");
                return;
            }
        }
    }

    private void VoiceButton_OnClick(object sender, RoutedEventArgs e) => ToggleVoiceInput();

    private void HandleMediaPlayPause()
    {
        AppLog.Info("MediaPlay: toggle voice listening=" + _voice.IsListening
            + " audioMode=" + (_audioSession?.Mode.ToString() ?? "?"));
        ToggleVoiceInput();
    }

    private void CheckSilenceAutoSend()
    {
        if (!_voice.IsListening || _voiceToggleBusy) return;
        if (!_hadSpeechText) return;
        if (_lastMicActivityUtc == DateTime.MinValue) return;

        var quietSec = (DateTime.UtcNow - _lastMicActivityUtc).TotalSeconds;
        if (quietSec < 3.5) return;

        AppLog.Info("SilenceAutoSend: quiet=" + quietSec.ToString("0.0")
            + "s textLen=" + (AnswerBox.Text?.Length ?? 0) + " — stop+send (HFP Play often missing)");
        StatusText.Text = "Тишина — отправка…";
        ToggleVoiceInput();
    }

    private async void ToggleVoiceInput()
    {
        if (_voiceToggleBusy) return;
        _voiceToggleBusy = true;
        try
        {
            if (_voice.IsListening)
            {
                AppLog.Info("Voice toggle: STOP live STT");
                try { _silenceAutoSendTimer?.Stop(); } catch { /* ignore */ }
                StatusText.Text = "Готово…";
                VoiceBtn.Content = "🎤";

                // Stop mic first — then reclaim A2DP/SMTC via AudioSessionManager.
                string text;
                try
                {
                    text = await Task.Run(() => _voice.StopAndTakeText()).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLog.Error("STT StopAndTakeText: " + ex);
                    text = string.Empty;
                }

                try { _audioSession?.ExitListening(rebuild: true); }
                catch (Exception ex) { AppLog.Warn("Voice toggle ExitListening: " + ex.Message); }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    AnswerBox.Text = text;
                    VoiceLogText.Text = "STT: " + text;
                    StatusText.Text = "Отправка…";
                    _ = SendCurrentAsync();
                }
                else
                {
                    StatusText.Text = "Пустая запись / не распознано";
                    VoiceLogText.Text = "STT: (пусто)";
                }

                return;
            }

            _tts.Stop();
            AppLog.Info("Voice toggle: START live STT headset=" + (_activeHeadsetName ?? "(none)"));
            AnswerBox.Text = string.Empty;
            VoiceLogText.Text = "STT: слушаю…";
            _eqPeak = 0;
            _hadSpeechText = false;
            _lastMicActivityUtc = DateTime.UtcNow;
            _audioSession?.EnterListening();
            _voice.Start("ru-RU", _activeHeadsetName);
            VoiceBtn.Content = "⏹";
            StatusText.Text = "🎤 Слушаю… (Play или ~3.5с тишины → отправить)";
            try { _silenceAutoSendTimer?.Start(); } catch { /* ignore */ }
        }
        finally
        {
            _voiceToggleBusy = false;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; return; }
            _tts.Stop();
            if (_voice.IsListening)
            {
                try { _silenceAutoSendTimer?.Stop(); } catch { /* ignore */ }
                _voice.Cancel();
                VoiceBtn.Content = "🎤";
                try { _audioSession?.ExitListening(rebuild: true); } catch { /* ignore */ }
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SpeakButton_OnClick(sender, e);
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            StartButton_OnClick(sender, e);
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            LogButton_OnClick(sender, e);
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            SettingsButton_OnClick(sender, e);
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.I)
        {
            AnswerBox.Focus();
            e.Handled = true;
        }
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
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = _prevStyle;
            ResizeMode = _prevResize;
            WindowState = _prevState;
            _isFullscreen = false;
        }
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings = SettingsStore.Load();
            AppLog.LogFolder = _settings.LogFolder;
            _tts.ApplySettings(_settings);
            _voice.ApplySettings(_settings);
            foreach (var b in _chat) ApplyBubbleStyle(b);
            _ = ConnectSupabaseAsync();
        }
    }

    private void LogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var w = new LogWindow { Owner = this };
        w.Show();
    }

    public void SetPlayHandlingSuspended(bool suspended)
    {
        // Legacy API — route through AudioSessionManager PlayTest mode.
        if (suspended) _audioSession?.BeginPlayTest();
        else _audioSession?.EndPlayTest();
    }

    public void PauseAudioHoldForMic() => _audioSession?.BeginMicTest();

    public void ResumeAudioHoldAfterMic() => _audioSession?.EndMicTest();

    public void CancelVoiceIfListening()
    {
        if (!_voice.IsListening) return;
        try
        {
            AppLog.Info("CancelVoiceIfListening — stop main STT before headset test");
            try { _silenceAutoSendTimer?.Stop(); } catch { /* ignore */ }
            _voice.Cancel();
            VoiceBtn.Content = "🎤";
            StatusText.Text = "Готово";
            _audioSession?.ExitListening(rebuild: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CancelVoiceIfListening: " + ex.Message);
        }
    }

    private void HeadsetTest_OnClick(object sender, RoutedEventArgs e)
    {
        var w = new HeadsetTestWindow { Owner = this };
        w.Show();
    }
}

public sealed class ChatBubbleVm : INotifyPropertyChanged
{
    public ChatBubbleKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Question { get; set; }
    public string? Words { get; set; }

    public double FontSize { get; set; } = 18;
    public Brush Brush { get; set; } = Brushes.White;
    public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;
    public double QuestionFontSize { get; set; } = 16;
    public double WordsFontSize { get; set; } = 40;
    public Brush QuestionBrush { get; set; } = Brushes.Gray;
    public Brush WordsBrush { get; set; } = Brushes.Gold;

    public Visibility TextVisibility =>
        string.IsNullOrWhiteSpace(Text) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility QuestionVisibility =>
        string.IsNullOrWhiteSpace(Question) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility WordsVisibility =>
        string.IsNullOrWhiteSpace(Words) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
