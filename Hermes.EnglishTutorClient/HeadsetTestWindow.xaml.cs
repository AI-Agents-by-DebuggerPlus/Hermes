using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.EnglishTutorClient.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hermes.EnglishTutorClient;

public partial class HeadsetTestWindow : Window
{
    private readonly ProgressBar[] _eqBars = new ProgressBar[16];
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly float[] _bands = new float[16];

    private MMDeviceEnumerator? _enum;
    private MMDevice? _captureDevice;
    private MMDevice? _renderDevice;
    private WasapiCapture? _capture;
    private WaveInEvent? _waveIn;
    private WaveFormat? _captureFormat;
    private WasapiOut? _waveOut;
    private WasapiOut? _scoHoldOut;
    private OfflineSttRecognizer? _offlineStt;
    private SpeechSynthesizer? _synth;
    private bool _micOn;
    private bool _toneOn;
    private bool _micStopping;
    private float _inPeak;
    private float _outPeak;
    private float _runPeakMax;
    private DateTime _lastPeakLogUtc = DateTime.MinValue;
    private DateTime _lastUiPeakLogUtc = DateTime.MinValue;
    private DateTime _micStartedUtc = DateTime.MinValue;
    private System.Threading.Timer? _watchdog;
    private MediaFocusClaimer? _playFocus;
    private MediaPlayHotkey? _playHotkey;
    private Action? _playTestHandler;
    private bool _playTestOn;
    private bool _playMicArmed = true; // true=🎤 waiting to "record", false=⏹ "recording"
    private int _playHitCount;
    private DateTime _lastPlayHitUtc = DateTime.MinValue;

    public HeadsetTestWindow()
    {
        InitializeComponent();
        for (var i = 0; i < _eqBars.Length; i++)
        {
            var bar = new ProgressBar
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 1,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x39)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B)),
            };
            _eqBars[i] = bar;
            EqGrid.Children.Add(bar);
        }
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try { _enum = new MMDeviceEnumerator(); }
        catch (Exception ex)
        {
            AppendStt("ERROR: MMDeviceEnumerator: " + ex.Message);
            AppLog.Error("HeadsetTest enum: " + ex);
        }

        _uiTimer.Tick += (_, __) => UpdateMetersUi();
        _uiTimer.Start();
        RefreshDevices();
        AppendStt("Окно теста гарнитуры готово. «Старт теста Play» — проверка кнопки Play на гарнитуре.");
        AppLog.Info("HeadsetTestWindow opened");
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _uiTimer.Stop();
        StopWatchdog();
        StopPlayTest();
        try
        {
            _micOn = false;
            try { StopCaptureOnly(); } catch { /* ignore */ }
            try { _offlineStt?.Stop(); } catch { /* ignore */ }
            try { _offlineStt?.Dispose(); } catch { /* ignore */ }
            _offlineStt = null;
            StopScoWakeWaveOut();
            StopScoHold();
            if (Owner is MainWindow mw)
                mw.ResumeAudioHoldAfterMic();
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetTest close cleanup: " + ex.Message);
        }

        StopTone();
        try { _enum?.Dispose(); } catch { /* ignore */ }
        _enum = null;
        AppLog.Info("HeadsetTestWindow closed");
    }

    private void PlayTestToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playTestOn) StopPlayTest();
        else StartPlayTest();
    }

    private void StartPlayTest()
    {
        if (_playTestOn) return;
        if (_micOn)
        {
            AppendStt("Сначала остановите mic + STT — Play-тест нужен на A2DP/SMTC");
            return;
        }

        try
        {
            if (Owner is not MainWindow mw || mw.AudioSession == null)
            {
                AppendStt("Play-тест FAILED: нет AudioSessionManager (откройте из MainWindow)");
                return;
            }

            mw.CancelVoiceIfListening();

            _playHitCount = 0;
            _playMicArmed = true;
            _lastPlayHitUtc = DateTime.MinValue;
            UpdatePlayMicIcon();
            PlayHitCounterText.Text = "Play: 0";

            // Single SMTC owner — do NOT create a second MediaFocusClaimer.
            _playTestHandler = () => OnPlayHit("AudioSession");
            mw.AudioSession.PlayTestPressed += _playTestHandler;
            mw.AudioSession.BeginPlayTest();

            _playTestOn = true;
            PlayTestBtn.Content = "■ Стоп теста Play";
            PlayTestDot.Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B));
            PlayTestStatusText.Text = "Жду Play на гарнитуре… (единый SMTC)";
            UpdatePlayTestDetail();
            AppendStt("Play-тест ON — нажмите Play на Pixel Buds (AudioSessionManager).");
            AppLog.Info("HeadsetTest Play-test started via AudioSessionManager");
        }
        catch (Exception ex)
        {
            AppendStt("Play-тест FAILED: " + ex.Message);
            AppLog.Error("HeadsetTest Play-test start: " + ex);
            StopPlayTest();
        }
    }

    private void StopPlayTest()
    {
        var wasOn = _playTestOn;
        _playTestOn = false;
        try
        {
            if (Owner is MainWindow mw && mw.AudioSession != null)
            {
                if (_playTestHandler != null)
                    mw.AudioSession.PlayTestPressed -= _playTestHandler;
                mw.AudioSession.EndPlayTest();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetTest StopPlayTest: " + ex.Message);
        }

        _playTestHandler = null;
        try { _playHotkey?.Dispose(); } catch { /* ignore */ }
        _playHotkey = null;
        try { _playFocus?.Dispose(); } catch { /* ignore */ }
        _playFocus = null;

        PlayTestBtn.Content = "▶ Старт теста Play";
        PlayTestDot.Fill = new SolidColorBrush(Color.FromRgb(0x84, 0x8E, 0x9C));
        _playMicArmed = true;
        UpdatePlayMicIcon();
        if (wasOn)
        {
            PlayTestStatusText.Text = "Тест Play остановлен. Нажатий: " + _playHitCount;
            AppendStt("Play-тест OFF (hits=" + _playHitCount + ")");
            AppLog.Info("HeadsetTest Play-test stopped hits=" + _playHitCount);
        }

        UpdatePlayTestDetail();
    }

    private void UpdatePlayMicIcon()
    {
        PlayMicIcon.Text = _playMicArmed ? "🎤" : "⏹";
    }

    private void OnPlayHit(string source)
    {
        // Debounce double delivery (SMTC + hotkey within 200ms)
        var now = DateTime.UtcNow;
        if ((now - _lastPlayHitUtc).TotalMilliseconds < 200)
        {
            AppendStt("Play debounced (" + source + ")");
            return;
        }

        _lastPlayHitUtc = now;
        _playHitCount++;
        _playMicArmed = !_playMicArmed;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdatePlayMicIcon();
            PlayHitCounterText.Text = "Play: " + _playHitCount;
            PlayTestDot.Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            var state = _playMicArmed ? "🎤 готов" : "⏹ запись";
            PlayTestStatusText.Text = "✓ Play #" + _playHitCount + " ← " + source
                + " → " + state
                + "  (" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + ")";
            UpdatePlayTestDetail();
            AppendStt("PLAY HIT #" + _playHitCount + " source=" + source + " icon=" + (_playMicArmed ? "mic" : "stop"));
            AppLog.Info("HeadsetTest PLAY HIT #" + _playHitCount + " source=" + source
                + " armed=" + _playMicArmed);

            // Flash back to waiting color
            var flash = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            flash.Tick += (_, __) =>
            {
                flash.Stop();
                if (_playTestOn)
                    PlayTestDot.Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0xB9, 0x0B));
            };
            flash.Start();

            try { _playFocus?.SetTransportStatus(MediaFocusClaimer.TransportStatus.Playing); }
            catch { /* ignore */ }
        }));
    }

    private void UpdatePlayTestDetail()
    {
        var smtc = _playFocus != null ? "ok" : "off";
        var hk = _playHotkey == null ? "off" : (_playHotkey.IsRegistered ? "reg" : "fail");
        PlayTestDetailText.Text = "SMTC=" + smtc
            + "  hotkey=" + hk
            + "  count=" + _playHitCount
            + "  icon=" + (_playMicArmed ? "mic" : "stop")
            + (_playTestOn ? "  | главное окно: Play suspended" : "");
    }

    private void RefreshDevices_OnClick(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        if (_enum == null) return;
        try
        {
            _renderDevice = PreferHeadsetRender(_enum)
                            ?? SafeDefault(_enum, DataFlow.Render, Role.Multimedia)
                            ?? SafeDefault(_enum, DataFlow.Render, Role.Console);
            _captureDevice = PreferHandsFree(_enum)
                             ?? SafeDefault(_enum, DataFlow.Capture, Role.Communications)
                             ?? SafeDefault(_enum, DataFlow.Capture, Role.Multimedia);

            if (_renderDevice != null) _renderDevice = _enum.GetDevice(_renderDevice.ID);
            if (_captureDevice != null) _captureDevice = _enum.GetDevice(_captureDevice.ID);

            OutDeviceText.Text = _renderDevice?.FriendlyName ?? "(нет)";
            OutStateText.Text = _renderDevice == null ? "" : "State=" + _renderDevice.State;
            InDeviceText.Text = _captureDevice?.FriendlyName ?? "(нет)";
            InStateText.Text = _captureDevice == null
                ? ""
                : "State=" + _captureDevice.State + "  id=" + ShortId(_captureDevice.ID);

            AppendStt("IN=" + InDeviceText.Text + " OUT=" + OutDeviceText.Text);
        }
        catch (Exception ex)
        {
            AppendStt("Refresh devices error: " + ex.Message);
            AppLog.Warn("HeadsetTest RefreshDevices: " + ex.Message);
        }
    }

    private static MMDevice? SafeDefault(MMDeviceEnumerator en, DataFlow flow, Role role)
    {
        try { return en.GetDefaultAudioEndpoint(flow, role); }
        catch { return null; }
    }

    private static MMDevice? PreferHeadsetRender(MMDeviceEnumerator en)
    {
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(d =>
            {
                var n = d.FriendlyName ?? "";
                return n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Headphones", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .OrderBy(d => (d.FriendlyName ?? "").IndexOf("Stereo", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ThenByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private static MMDevice? PreferHandsFree(MMDeviceEnumerator en)
    {
        return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged)
            .Where(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(d => (d.FriendlyName ?? "").IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) < 0)
            .OrderBy(d => d.State == DeviceState.Active ? 0 : 1)
            .ThenByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private void PlayTone_OnClick(object sender, RoutedEventArgs e)
    {
        if (_toneOn)
        {
            StopTone();
            AppendStt("Playback: STOP");
            AppLog.Info("HeadsetTest play tone STOP");
            return;
        }

        if (_micOn)
        {
            AppendStt("Сначала остановите mic + STT (A2DP и Hands-Free конфликтуют)");
            return;
        }

        try
        {
            RefreshDevices();
            if (_renderDevice == null)
            {
                AppendStt("ERROR: нет playback-устройства");
                return;
            }

            if (_renderDevice != null)
                AudioPolicyConfig.TryClaimAllRoles(_renderDevice);

            var tone = new SignalGenerator(44100, 1)
            {
                Gain = 0.2,
                Frequency = 440,
                Type = SignalGeneratorType.Sin,
            };
            _waveOut = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, 100);
            _waveOut.Init(tone);
            _waveOut.Play();
            _toneOn = true;
            _outPeak = 0.35f;
            ToneBtn.Content = "■ Стоп динамика";
            AppendStt("Playback: 440 Hz ON → " + OutDeviceText.Text);
            AppLog.Info("HeadsetTest play tone on " + OutDeviceText.Text);

            try
            {
                _synth?.Dispose();
                _synth = new SpeechSynthesizer();
                _synth.SetOutputToDefaultAudioDevice();
                _synth.Volume = 80;
                _synth.SpeakAsync("Audio test");
                AppendStt("SAPI TTS: «Audio test»");
            }
            catch (Exception ex)
            {
                AppendStt("SAPI TTS failed: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            AppendStt("Play tone error: " + ex.Message);
            AppLog.Error("HeadsetTest tone: " + ex);
            StopTone();
        }
    }

    private void StopTone()
    {
        _toneOn = false;
        try { ToneBtn.Content = "▶ Тест динамика"; } catch { /* ignore */ }
        try { _synth?.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        try { _synth?.Dispose(); } catch { /* ignore */ }
        _synth = null;
        try { _waveOut?.Stop(); } catch { /* ignore */ }
        try { _waveOut?.Dispose(); } catch { /* ignore */ }
        _waveOut = null;
        _outPeak = 0;
    }

    private async void MicToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_micStopping) return;
        if (_micOn) await StopMicAsync();
        else await StartMicAsync();
    }

    private async Task StartMicAsync()
    {
        RefreshDevices();
        if (_captureDevice == null)
        {
            AppendStt("ERROR: нет capture-устройства");
            return;
        }

        MicBtn.IsEnabled = false;
        try
        {
            if (_playTestOn)
            {
                AppendStt("Сначала остановите тест Play (A2DP/SMTC нужен для кнопки)");
                return;
            }

            StopTone();
            StopScoHold();
            StopCaptureOnly();
            if (Owner is MainWindow mw)
            {
                mw.CancelVoiceIfListening();
                mw.PauseAudioHoldForMic();
            }

            var captureId = _captureDevice.ID;
            var captureName = _captureDevice.FriendlyName ?? "";
            var headsetCapture = IsHeadsetCaptureName(captureName);
            AppendStt("Capture: " + captureName
                + (headsetCapture ? "" : " ⚠ не гарнитура"));

            if (headsetCapture)
            {
                try
                {
                    AudioPolicyConfig.TryClaimAllRoles(GetFreshMMDevice(captureId));
                    var hfRender = PreferHandsFreeRender(_enum!);
                    if (hfRender != null)
                        AudioPolicyConfig.TryClaimAllRoles(GetFreshMMDevice(hfRender.ID));
                }
                catch { /* ignore */ }

                // Wake HFP SCO via classic WaveOut (WasapiOut on HF returns 0x88890008 here).
                StartScoWakeWaveOut();
                await Task.Delay(1000);
            }
            else
            {
                AppendStt("Гарнитура Hands-Free не Active — тест идёт с ПК-микрофона");
                AppLog.Warn("HeadsetTest: no BT Hands-Free — using PC mic");
                await Task.Delay(300);
            }

            _runPeakMax = 0;
            _lastPeakLogUtc = DateTime.MinValue;
            _micStartedUtc = DateTime.UtcNow;

            if (!TryStartWaveInCapture(requireHeadset: headsetCapture))
            {
                if (headsetCapture)
                    throw new InvalidOperationException(
                        "Не удалось открыть микрофон гарнитуры (WaveIn). ПК-мик не используем.");
                await OpenAndStartWasapiAsync(captureId);
            }

            var fmt = _captureFormat ?? new WaveFormat(8000, 16, 1);
            AppendStt("Mic ON via " + (_waveIn != null ? "WaveIn" : "WASAPI")
                + " " + fmt.SampleRate + "Hz");
            AppLog.Info("HeadsetTest mic started via=" + (_waveIn != null ? "WaveIn" : "WASAPI")
                + " rate=" + fmt.SampleRate + " headset=" + headsetCapture);

            _micOn = true;
            MicBtn.Content = "■ Стоп mic + STT";
            StartOfflineStt(fmt);
            StartWatchdog();

            if (headsetCapture)
                _ = WatchHeadsetPeakAsync();
        }
        catch (Exception ex)
        {
            AppendStt("Capture FAILED: " + ex.Message);
            AppLog.Error("HeadsetTest capture failed: " + ex.Message);
            StopScoWakeWaveOut();
            StopScoHold();
            await StopMicAsync();
        }
        finally
        {
            MicBtn.IsEnabled = true;
        }
    }

    private async Task WatchHeadsetPeakAsync()
    {
        try
        {
            await Task.Delay(2000);
            if (!_micOn || _micStopping) return;
            if (_runPeakMax >= 0.02f) return;
            AppendStt("WARN: гарнитура открыта, но peak=0 — HFP/SCO не отдаёт звук. "
                + "Отключите A2DP в другом приложении / переподключите buds.");
            AppLog.Warn("HeadsetTest headset WaveIn silent peakMax="
                + _runPeakMax.ToString("0.000", CultureInfo.InvariantCulture));
        }
        catch { /* ignore */ }
    }

    private static bool IsHeadsetCaptureName(string? n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        if (n.IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private WaveOutEvent? _scoWaveOut;

    private void StartScoWakeWaveOut()
    {
        StopScoWakeWaveOut();
        try
        {
            var outIdx = FindHandsFreeWaveOutDevice();
            if (outIdx < 0)
            {
                AppLog.Warn("HeadsetTest SCO WaveOut: no HF render device");
                return;
            }

            var fmt = new WaveFormat(8000, 16, 1);
            var silence = new SignalGenerator(fmt.SampleRate, 1)
            {
                Gain = 0.0003,
                Frequency = 20,
                Type = SignalGeneratorType.Sin,
            };
            _scoWaveOut = new WaveOutEvent { DeviceNumber = outIdx, DesiredLatency = 100 };
            _scoWaveOut.Init(silence);
            _scoWaveOut.Play();
            AppLog.Info("HeadsetTest SCO WaveOut#" + outIdx + " wake @" + fmt.SampleRate + "Hz");
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetTest SCO WaveOut failed: " + ex.Message);
            StopScoWakeWaveOut();
        }
    }

    private void StopScoWakeWaveOut()
    {
        try { _scoWaveOut?.Stop(); } catch { /* ignore */ }
        try { _scoWaveOut?.Dispose(); } catch { /* ignore */ }
        _scoWaveOut = null;
    }

    private static int FindHandsFreeWaveOutDevice()
    {
        var best = -1;
        var bestScore = -1;
        for (var i = 0; i < WaveOut.DeviceCount; i++)
        {
            WaveOutCapabilities caps;
            try { caps = WaveOut.GetCapabilities(i); }
            catch { continue; }
            var n = caps.ProductName ?? "";
            var score = 0;
            if (n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
            if (n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0) score += 8;
            if (n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            if (n.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return bestScore > 0 ? best : -1;
    }

    private MMDevice GetFreshMMDevice(string deviceId)
    {
        if (_enum != null)
            return _enum.GetDevice(deviceId);
        using var en = new MMDeviceEnumerator();
        return en.GetDevice(deviceId);
    }

    private bool TryStartWaveInCapture(bool requireHeadset)
    {
        LogWaveInDevicesOnce();
        var candidates = EnumerateWaveInCandidates(requireHeadset).ToList();
        if (candidates.Count == 0)
        {
            AppLog.Warn("HeadsetTest WaveIn: no candidate devices requireHeadset=" + requireHeadset);
            return false;
        }

        var formats = new[]
        {
            new WaveFormat(8000, 16, 1),
            new WaveFormat(8000, 8, 1),
            new WaveFormat(16000, 16, 1),
            new WaveFormat(11025, 16, 1),
        };

        foreach (var idx in candidates)
        {
            string name;
            try { name = WaveIn.GetCapabilities(idx).ProductName ?? ("#" + idx); }
            catch { name = "#" + idx; }

            foreach (var fmt in formats)
            {
                WaveInEvent? wave = null;
                try
                {
                    wave = new WaveInEvent
                    {
                        DeviceNumber = idx,
                        WaveFormat = fmt,
                        BufferMilliseconds = 50,
                    };
                    wave.DataAvailable += OnCaptureData;
                    wave.RecordingStopped += (_, e2) =>
                    {
                        if (e2.Exception != null)
                            AppLog.Warn("HeadsetTest WaveIn stopped: " + e2.Exception.Message);
                    };
                    wave.StartRecording();
                    _waveIn = wave;
                    _captureFormat = fmt;
                    AppLog.Info("HeadsetTest WaveIn#" + idx + " " + name
                        + " " + fmt.SampleRate + "Hz/" + fmt.BitsPerSample + "bit");
                    AppendStt("WaveIn: " + name + " @" + fmt.SampleRate + "Hz");
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (wave != null)
                        {
                            try { wave.DataAvailable -= OnCaptureData; } catch { /* ignore */ }
                            wave.Dispose();
                        }
                    }
                    catch { /* ignore */ }

                    if (fmt.SampleRate == 8000 && fmt.BitsPerSample == 16)
                        AppLog.Warn("HeadsetTest WaveIn#" + idx + " 8k: " + ex.Message);
                }
            }
        }

        AppLog.Warn("HeadsetTest WaveIn: all devices/formats failed");
        return false;
    }

    private static int _loggedWaveInDevices;

    private static void LogWaveInDevicesOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _loggedWaveInDevices, 1) != 0) return;
        try
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                try
                {
                    var c = WaveIn.GetCapabilities(i);
                    parts.Add(i + ":" + (c.ProductName ?? "?"));
                }
                catch { /* ignore */ }
            }

            AppLog.Info("WaveIn devices: " + string.Join(" | ", parts));
        }
        catch (Exception ex)
        {
            AppLog.Warn("WaveIn list failed: " + ex.Message);
        }
    }

    private static System.Collections.Generic.IEnumerable<int> EnumerateWaveInCandidates(bool requireHeadset)
    {
        var scored = new System.Collections.Generic.List<(int Idx, int Score)>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            WaveInCapabilities caps;
            try { caps = WaveIn.GetCapabilities(i); }
            catch { continue; }
            var n = caps.ProductName ?? "";
            if (n.IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (n.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            var isHeadset = n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0;
            var isPcMic = n.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0
                          || (n.IndexOf("Microphone", StringComparison.OrdinalIgnoreCase) >= 0 && !isHeadset);

            if (requireHeadset)
            {
                if (!isHeadset) continue;
            }
            else if (isPcMic)
            {
                // OK as fallback when no headset
            }
            else if (!isHeadset && !isPcMic)
            {
                continue;
            }

            var score = 0;
            if (n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
            if (n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0) score += 12;
            if (n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0) score += 8;
            if (n.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0) score += 6;
            if (isPcMic) score += 1;
            if (score > 0)
                scored.Add((i, score));
        }

        return scored.OrderByDescending(x => x.Score).Select(x => x.Idx);
    }

    private async Task OpenAndStartWasapiAsync(string deviceId, int maxAttempts = 3)
    {
        Exception? lastEx = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            WasapiCapture? capture = null;
            try
            {
                var device = GetFreshMMDevice(deviceId);
                _captureDevice = device;
                capture = new WasapiCapture(device, false, 100);
                _captureFormat = capture.WaveFormat;
                capture.DataAvailable += OnCaptureData;
                capture.StartRecording();
                _capture = capture;
                AppLog.Info("HeadsetTest WASAPI started attempt=" + attempt);
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                try { capture?.Dispose(); } catch { /* ignore */ }
                AppLog.Warn("HeadsetTest WASAPI attempt " + attempt + "/" + maxAttempts + ": " + ex.Message);
                if (attempt < maxAttempts)
                    await Task.Delay(350 * attempt);
            }
        }

        throw lastEx ?? new InvalidOperationException("WASAPI capture failed");
    }

    private void StopCaptureOnly()
    {
        try
        {
            if (_waveIn != null)
            {
                try { _waveIn.DataAvailable -= OnCaptureData; } catch { /* ignore */ }
                try { _waveIn.StopRecording(); } catch { /* ignore */ }
                try { _waveIn.Dispose(); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        _waveIn = null;

        try
        {
            if (_capture != null)
            {
                try { _capture.DataAvailable -= OnCaptureData; } catch { /* ignore */ }
                try { _capture.StopRecording(); } catch { /* ignore */ }
                try { _capture.Dispose(); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        _capture = null;
        _captureFormat = null;
    }

    private async Task StopMicAsync()
    {
        if (_micStopping) return;
        AppLog.Info("HeadsetTest StopMicAsync begin");
        AppendStt("Stopping mic/STT…");
        _micStopping = true;
        _micOn = false;
        MicBtn.IsEnabled = false;
        MicBtn.Content = "… стоп";

        StopWatchdog();
        var offline = _offlineStt;
        _offlineStt = null;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    offline?.Stop();
                    offline?.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Warn("HeadsetTest STT stop: " + ex.Message);
                }

                try { StopCaptureOnly(); }
                catch (Exception ex)
                {
                    AppLog.Warn("HeadsetTest capture stop: " + ex.Message);
                }
            });

            StopScoWakeWaveOut();
            StopScoHold();

            AppLog.Info("HeadsetTest peakMax=" + _runPeakMax.ToString("0.000", CultureInfo.InvariantCulture));
            AppendStt("peakMax=" + _runPeakMax.ToString("0.000", CultureInfo.InvariantCulture));
            _inPeak = 0;
            Array.Clear(_bands, 0, _bands.Length);
            AppendStt("Capture stopped");

            if (Owner is MainWindow mw)
                mw.ResumeAudioHoldAfterMic();
        }
        catch (Exception ex)
        {
            AppendStt("Stop FAILED: " + ex.Message);
            AppLog.Error("HeadsetTest StopMicAsync: " + ex.Message);
        }
        finally
        {
            MicBtn.Content = "● Старт mic + STT";
            MicBtn.IsEnabled = true;
            _micStopping = false;
        }
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var fmt = _captureFormat ?? _waveIn?.WaveFormat ?? _capture?.WaveFormat;
        if (fmt == null) return;

        var peak = Peak(e.Buffer, e.BytesRecorded, fmt);
        _inPeak = Math.Max(peak, _inPeak * 0.85f);
        if (peak > _runPeakMax) _runPeakMax = peak;
        UpdateBands(e.Buffer, e.BytesRecorded, fmt);

        var now = DateTime.UtcNow;
        if ((now - _lastPeakLogUtc).TotalSeconds >= 2.0)
        {
            _lastPeakLogUtc = now;
            AppLog.Info("HeadsetTest peak=" + peak.ToString("0.000", CultureInfo.InvariantCulture)
                + " max=" + _runPeakMax.ToString("0.000", CultureInfo.InvariantCulture));
        }

        if (_offlineStt == null) return;
        var pcm = ToPcm16Mono(e.Buffer, e.BytesRecorded, fmt);
        if (pcm.Length > 0)
            _offlineStt.WritePcm(pcm, pcm.Length);
    }

    private void StartOfflineStt(WaveFormat captureFmt)
    {
        StopStt();
        try
        {
            var sampleRate = captureFmt.SampleRate > 0 ? captureFmt.SampleRate : 16000;
            var settings = SettingsStore.Load();
            _offlineStt = new OfflineSttRecognizer(
                sampleRate, 16, 1, TimeSpan.FromSeconds(2.5), settings: settings);
            _offlineStt.SegmentRecognized += text =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AppendStt("✓ " + text);
                        AppLog.Info("HeadsetTest STT recognized: " + text);
                    }));
                }
                catch { /* ignore */ }
            };
            _offlineStt.SegmentRejected += reason =>
            {
                if (reason != null && reason.StartsWith("(below", StringComparison.Ordinal))
                    return;
            };
            _offlineStt.RecognitionError += ex =>
            {
                AppLog.Error("HeadsetTest STT segment error: " + ex);
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        AppendStt("STT error: " + ex.Message)));
                }
                catch { /* ignore */ }
            };

            _offlineStt.Start("ru-RU");
            var engine = _offlineStt.EngineName;
            AppendStt("STT started (" + engine + ") @" + sampleRate + "Hz cultureReq=ru-RU");
            AppLog.Info("HeadsetTest STT started engine=" + engine + " cultureReq=ru-RU");
        }
        catch (Exception ex)
        {
            AppendStt("STT start failed: " + ex.Message);
            AppLog.Error("HeadsetTest STT: " + ex);
        }
    }

    private void StartScoHold(MMDevice hfRender)
    {
        StopScoHold();
        try
        {
            var silence = new SignalGenerator(16000, 1)
            {
                Gain = 0.0002,
                Frequency = 20,
                Type = SignalGeneratorType.Sin,
            };
            _scoHoldOut = new WasapiOut(hfRender, AudioClientShareMode.Shared, true, 100);
            _scoHoldOut.Init(silence);
            _scoHoldOut.Play();
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetTest SCO hold failed: " + ex.Message);
            StopScoHold();
        }
    }

    private void StopScoHold()
    {
        try { _scoHoldOut?.Stop(); } catch { /* ignore */ }
        try { _scoHoldOut?.Dispose(); } catch { /* ignore */ }
        _scoHoldOut = null;
    }

    private void StopStt()
    {
        AppLog.Info("HeadsetTest StopStt begin (non-blocking)");
        var offline = _offlineStt;
        _offlineStt = null;
        if (offline == null) return;

        Task.Run(() =>
        {
            try
            {
                offline.Stop();
                offline.Dispose();
                AppLog.Info("HeadsetTest StopStt background cleanup done");
            }
            catch (Exception ex)
            {
                AppLog.Error("HeadsetTest StopStt background cleanup failed: " + ex);
            }
        });
    }

    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = new System.Threading.Timer(_ =>
        {
            try
            {
                if (!_micOn || _micStopping) return;
                if ((DateTime.UtcNow - _micStartedUtc) < TimeSpan.FromSeconds(120))
                    return;

                AppLog.Error("Watchdog: mic session exceeded 120s without stop — forcing StopMicAsync from background");
                try
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        AppendStt("Watchdog: force stop after 120s");
                        await StopMicAsync();
                    }));
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Watchdog dispatch failed: " + ex.Message);
                    _micOn = false;
                    try { _offlineStt?.Stop(); } catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Watchdog: " + ex.Message);
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void StopWatchdog()
    {
        try { _watchdog?.Dispose(); } catch { /* ignore */ }
        _watchdog = null;
    }

    private static MMDevice? PreferHandsFreeRender(MMDeviceEnumerator en)
    {
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged)
            .Where(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(d => d.State == DeviceState.Active ? 0 : 1)
            .ThenByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private void UpdateBands(byte[] buffer, int bytes, WaveFormat fmt)
    {
        float[] samples;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample == 32)
        {
            var n = bytes / 4;
            samples = new float[n];
            for (var i = 0; i < n; i++)
                samples[i] = Math.Abs(BitConverter.ToSingle(buffer, i * 4));
        }
        else
        {
            var n = bytes / 2;
            samples = new float[n];
            for (var i = 0; i < n; i++)
            {
                var s = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
                samples[i] = Math.Abs(s) / 32768f;
            }
        }

        if (samples.Length == 0) return;
        var chunk = Math.Max(1, samples.Length / _bands.Length);
        for (var b = 0; b < _bands.Length; b++)
        {
            float sum = 0;
            var start = b * chunk;
            var end = Math.Min(samples.Length, start + chunk);
            for (var i = start; i < end; i++) sum += samples[i];
            var avg = sum / Math.Max(1, end - start);
            _bands[b] = Math.Max(avg, _bands[b] * 0.7f);
        }
    }

    private void UpdateMetersUi()
    {
        // No AudioMeterInformation / COM on UI thread — peak comes from OnCaptureData only.
        InLevelBar.Value = Math.Min(1, _inPeak * 1.4);
        OutLevelBar.Value = Math.Min(1, _outPeak * 1.4);
        _inPeak *= 0.92f;
        _outPeak *= 0.9f;
        for (var i = 0; i < _eqBars.Length; i++)
        {
            _eqBars[i].Value = Math.Min(1, _bands[i] * 2.2);
            _bands[i] *= 0.85f;
        }

        if (_micOn)
            InStateText.Text = "peak=" + _inPeak.ToString("0.000", CultureInfo.InvariantCulture)
                + "  offline-STT";
    }

    private void ClearLog_OnClick(object sender, RoutedEventArgs e) => SttLogBox.Clear();

    private void AppendStt(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        SttLogBox.AppendText("[" + stamp + "] " + line + Environment.NewLine);
        SttLogBox.ScrollToEnd();
    }

    private static byte[] ToPcm16Mono(byte[] buffer, int bytes, WaveFormat fmt)
    {
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample == 32)
        {
            var frames = bytes / (4 * Math.Max(1, fmt.Channels));
            var pcm = new byte[frames * 2];
            for (var f = 0; f < frames; f++)
            {
                float sum = 0;
                for (var ch = 0; ch < fmt.Channels; ch++)
                    sum += BitConverter.ToSingle(buffer, (f * fmt.Channels + ch) * 4);
                var sample = sum / fmt.Channels;
                var s = (short)Math.Max(-32768, Math.Min(32767, (int)(sample * 32767f)));
                pcm[f * 2] = (byte)(s & 0xFF);
                pcm[f * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }

            return pcm;
        }

        if (fmt.Channels == 1 && fmt.BitsPerSample == 16)
        {
            var copy = new byte[bytes];
            Buffer.BlockCopy(buffer, 0, copy, 0, bytes);
            return copy;
        }

        // 16-bit multi-channel → mono
        var frameBytes = 2 * Math.Max(1, fmt.Channels);
        var n = bytes / frameBytes;
        var outPcm = new byte[n * 2];
        for (var f = 0; f < n; f++)
        {
            int sum = 0;
            for (var ch = 0; ch < fmt.Channels; ch++)
            {
                var i = f * frameBytes + ch * 2;
                sum += (short)(buffer[i] | (buffer[i + 1] << 8));
            }

            var s = (short)(sum / fmt.Channels);
            outPcm[f * 2] = (byte)(s & 0xFF);
            outPcm[f * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        return outPcm;
    }

    private static float Peak(byte[] buffer, int bytes, WaveFormat fmt)
    {
        float max = 0;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample == 32)
        {
            for (var i = 0; i + 3 < bytes; i += 4)
            {
                var a = Math.Abs(BitConverter.ToSingle(buffer, i));
                if (a > max) max = a;
            }
        }
        else
        {
            for (var i = 0; i + 1 < bytes; i += 2)
            {
                var s = (short)(buffer[i] | (buffer[i + 1] << 8));
                var a = Math.Abs(s) / 32768f;
                if (a > max) max = a;
            }
        }

        return max;
    }

    private static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return id!.Length <= 40 ? id : id.Substring(0, 40) + "…";
    }
}
