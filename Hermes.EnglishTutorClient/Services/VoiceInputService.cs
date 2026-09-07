using System;
using System.Globalization;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Live dictation: Google / Azure cloud STT when keys configured, otherwise SAPI.
/// Prefers Hands-Free mic; cloud path uses 8 kHz WaveIn + SCO wake (same as HeadsetTest).
/// </summary>
public sealed class VoiceInputService : IDisposable
{
    private readonly object _gate = new();
    private readonly StringBuilder _final = new();
    private SpeechRecognitionEngine? _sapi;
    private WaveInEvent? _waveIn;
    private WaveOutEvent? _scoWaveOut;
    private OfflineSttRecognizer? _offline;
    private MMDeviceEnumerator? _mmEnum;
    private bool _listening;
    private string _sttEngine = SttEnginePicker.Sapi;
    private string _lastHypothesis = string.Empty;
    private string _deviceLabel = string.Empty;
    private int _hypEvents;
    private int _recEvents;
    private int _rejectEvents;
    private int _restartCount;
    private AppSettings? _settings;
    private float _peak;

    public bool IsListening
    {
        get { lock (_gate) return _listening; }
    }

    /// <summary>Full text so far (finals + current hypothesis).</summary>
    public event Action<string>? PartialResult;
    /// <summary>One recognized phrase (for UI log line).</summary>
    public event Action<string>? SegmentRecognized;
    public event Action<string>? StatusChanged;
    /// <summary>Live mic level 0..1 for equalizer.</summary>
    public event Action<float>? PeakLevel;

    public void ApplySettings(AppSettings settings) => _settings = settings;

    public void Start(string cultureName = "ru-RU", string? preferredHeadsetName = null)
    {
        lock (_gate)
        {
            if (_listening)
            {
                AppLog.Info("STT Start ignored — already listening");
                return;
            }

            StopCore_NoLock(clearText: true);
            _hypEvents = 0;
            _recEvents = 0;
            _rejectEvents = 0;
            _restartCount = 0;

            var settings = _settings ?? SettingsStore.Load();
            _settings = settings;
            _sttEngine = SttEnginePicker.Pick(settings);

            AppLog.Info("STT Start cultureReq=" + cultureName + " engine=" + _sttEngine);

            try
            {
                _mmEnum = new MMDeviceEnumerator();
                var mic = PickHandsFree(_mmEnum, preferredHeadsetName);
                if (mic != null)
                {
                    _deviceLabel = mic.FriendlyName ?? "";
                    AppLog.Info("STT mic=" + _deviceLabel + " state=" + mic.State);
                    AudioPolicyConfig.TryClaimAllRoles(mic);
                    try
                    {
                        var hfRender = _mmEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged)
                            .FirstOrDefault(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0
                                                 && (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
                            ?? _mmEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged)
                                .FirstOrDefault(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (hfRender != null)
                            AudioPolicyConfig.TryClaimAllRoles(hfRender);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("STT claim HF render: " + ex.Message);
                    }

                    Thread.Sleep(500);
                    try
                    {
                        var defC = _mmEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        if (defC != null)
                            AppLog.Info("STT default-comm=" + defC.FriendlyName
                                + " peak=" + SafePeak(defC).ToString("0.000", CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("STT read default capture: " + ex.Message);
                    }
                }
                else
                {
                    AppLog.Warn("STT: Hands-Free not found — using system default mic");
                    _deviceLabel = "default";
                }

                if (_sttEngine != SttEnginePicker.Sapi)
                    StartCloud_NoLock(cultureName, preferredHeadsetName);
                else
                    StartSapi_NoLock(cultureName);

                _listening = true;
                var tip = string.IsNullOrWhiteSpace(_deviceLabel) ? "mic" : ShortName(_deviceLabel);
                StatusChanged?.Invoke("🎤 Слушаю… (" + tip + ", " + _sttEngine + ")");
                AppLog.Info("STT listening engine=" + _sttEngine);
            }
            catch (Exception ex)
            {
                AppLog.Error("STT live Start failed: " + ex);
                StatusChanged?.Invoke("Голосовой ввод недоступен: " + ex.Message);
                StopCore_NoLock(clearText: true);
            }
        }
    }

    private void StartCloud_NoLock(string cultureName, string? preferredHeadsetName)
    {
        // HFP needs SCO wake + 8 kHz (16 kHz WaveIn opens but peak stays 0 on Pixel HF).
        StartScoWake();
        Thread.Sleep(900);

        if (!TryStartWaveIn(preferredHeadsetName, out var fmt))
            throw new InvalidOperationException("Не удалось открыть микрофон (WaveIn)");

        _offline = new OfflineSttRecognizer(
            sampleRate: fmt.SampleRate,
            bitsPerSample: fmt.BitsPerSample,
            channels: fmt.Channels,
            segmentLength: TimeSpan.FromSeconds(2.5),
            settings: _settings);
        _offline.SegmentRecognized += OnCloudSegment;
        _offline.SegmentRejected += OnCloudRejected;
        _offline.RecognitionError += OnCloudError;
        _offline.Start(cultureName);
        AppLog.Info("STT " + _sttEngine + " capture " + fmt.SampleRate + "Hz/" + fmt.BitsPerSample + "bit ch=" + fmt.Channels);
    }

    private void StartScoWake()
    {
        StopScoWake();
        try
        {
            var outIdx = FindHandsFreeWaveOutDevice();
            if (outIdx < 0)
            {
                AppLog.Warn("STT SCO WaveOut: no HF render device");
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
            AppLog.Info("STT SCO WaveOut#" + outIdx + " wake @" + fmt.SampleRate + "Hz");
        }
        catch (Exception ex)
        {
            AppLog.Warn("STT SCO WaveOut failed: " + ex.Message);
            StopScoWake();
        }
    }

    private void StopScoWake()
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

    private void StartSapi_NoLock(string cultureName)
    {
        SpeechRecognizerPicker.LogInstalledRecognizersOnce();
        var info = SpeechRecognizerPicker.Pick(cultureName);
        if (info == null)
            throw new InvalidOperationException("No speech recognizer installed");

        if (!string.Equals(info.Culture.Name, cultureName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(info.Culture.Name, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Warn("STT: using en-US (ru-RU speech pack not installed). Speak English clearly.");
            StatusChanged?.Invoke("⚠ Нет пакета ru-RU — говорите по-английски (en-US)");
        }

        AppLog.Info("STT recognizer=" + info.Description + " culture=" + info.Culture.Name
            + " (cultureReq=" + cultureName + ")");
        _sapi = new SpeechRecognitionEngine(info);
        _sapi.InitialSilenceTimeout = TimeSpan.FromMinutes(5);
        _sapi.BabbleTimeout = TimeSpan.FromMinutes(5);
        _sapi.EndSilenceTimeout = TimeSpan.FromSeconds(1.5);
        _sapi.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(2);

        _sapi.SpeechHypothesized += OnHypothesized;
        _sapi.SpeechRecognized += OnRecognized;
        _sapi.SpeechRecognitionRejected += OnRejected;
        _sapi.RecognizeCompleted += OnRecognizeCompleted;

        _sapi.LoadGrammar(new DictationGrammar());
        _sapi.SetInputToDefaultAudioDevice();
        _sapi.RecognizeAsync(RecognizeMode.Multiple);
    }

    private bool TryStartWaveIn(string? preferredHeadsetName, out WaveFormat fmt)
    {
        fmt = new WaveFormat(8000, 16, 1);
        // Prefer 8 kHz first — HFP Hands-Free on Pixel often silent at 16 kHz.
        var formats = new[]
        {
            new WaveFormat(8000, 16, 1),
            new WaveFormat(16000, 16, 1),
            new WaveFormat(11025, 16, 1),
        };

        var candidates = EnumerateWaveInCandidates(preferredHeadsetName).ToList();
        if (candidates.Count == 0)
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
                candidates.Add(i);
        }

        foreach (var idx in candidates)
        {
            string name;
            try { name = WaveIn.GetCapabilities(idx).ProductName ?? ("#" + idx); }
            catch { name = "#" + idx; }

            foreach (var candidate in formats)
            {
                WaveInEvent? wave = null;
                try
                {
                    wave = new WaveInEvent
                    {
                        DeviceNumber = idx,
                        WaveFormat = candidate,
                        BufferMilliseconds = 50,
                    };
                    wave.DataAvailable += OnWaveData;
                    wave.StartRecording();
                    _waveIn = wave;
                    fmt = candidate;
                    _deviceLabel = name;
                    AppLog.Info("STT WaveIn#" + idx + " " + name + " @" + candidate.SampleRate + "Hz");
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (wave != null)
                        {
                            try { wave.DataAvailable -= OnWaveData; } catch { /* ignore */ }
                            wave.Dispose();
                        }
                    }
                    catch { /* ignore */ }

                    AppLog.Warn("STT WaveIn#" + idx + " " + candidate.SampleRate + "Hz: " + ex.Message);
                }
            }
        }

        return false;
    }

    private static System.Collections.Generic.IEnumerable<int> EnumerateWaveInCandidates(string? preferredHeadsetName)
    {
        var scored = new System.Collections.Generic.List<(int Idx, int Score)>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            WaveInCapabilities caps;
            try { caps = WaveIn.GetCapabilities(i); }
            catch { continue; }
            var n = caps.ProductName ?? "";
            if (n.IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            var score = 0;
            if (n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            if (n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
            if (!string.IsNullOrWhiteSpace(preferredHeadsetName))
            {
                foreach (var t in preferredHeadsetName!.Split(new[] { ' ', '\'', '#', '-' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (t.Length >= 3 && n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 2;
                }
            }

            if (score > 0)
                scored.Add((i, score));
        }

        return scored.OrderByDescending(x => x.Score).Select(x => x.Idx);
    }

    private void OnWaveData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var peak = PeakPcm16(e.Buffer, e.BytesRecorded);
        _peak = Math.Max(peak, _peak * 0.85f);
        try { PeakLevel?.Invoke(_peak); } catch { /* ignore */ }

        if (_offline == null) return;
        try
        {
            _offline.WritePcm(e.Buffer, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            AppLog.Warn("STT WritePcm: " + ex.Message);
        }
    }

    private static float PeakPcm16(byte[] buffer, int bytes)
    {
        short max = 0;
        for (var i = 0; i + 1 < bytes; i += 2)
        {
            var s = (short)(buffer[i] | (buffer[i + 1] << 8));
            var a = s < 0 ? (short)-s : s;
            if (a > max) max = a;
        }

        return max / 32768f;
    }

    private void OnCloudSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Interlocked.Increment(ref _recEvents);
        AppLog.Info("STT " + _sttEngine + " #" + _recEvents + " text=" + Truncate(text, 120));
        lock (_gate)
        {
            if (_final.Length > 0) _final.Append(' ');
            _final.Append(text.Trim());
            _lastHypothesis = string.Empty;
            var display = BuildDisplayText();
            try
            {
                SegmentRecognized?.Invoke(text.Trim());
                PartialResult?.Invoke(display);
                StatusChanged?.Invoke("🎤 " + display);
            }
            catch (Exception ex)
            {
                AppLog.Warn("STT PartialResult: " + ex.Message);
            }
        }
    }

    private void OnCloudRejected(string reason)
    {
        Interlocked.Increment(ref _rejectEvents);
        if (_rejectEvents <= 3 || _rejectEvents % 10 == 0)
            AppLog.Info("STT " + _sttEngine + " rejected #" + _rejectEvents + " " + reason);
    }

    private void OnCloudError(Exception ex)
    {
        AppLog.Error("STT " + _sttEngine + " error: " + ex.Message);
        try { StatusChanged?.Invoke(_sttEngine + " STT: " + Truncate(ex.Message, 80)); }
        catch { /* ignore */ }
    }

    public string StopAndTakeText()
    {
        lock (_gate)
        {
            if (!_listening)
            {
                AppLog.Info("STT Stop ignored — not listening");
                return string.Empty;
            }

            AppLog.Info("STT Stop requested hyp=" + _hypEvents
                + " rec=" + _recEvents
                + " rej=" + _rejectEvents
                + " finalsLen=" + _final.Length
                + " lastHypLen=" + _lastHypothesis.Length
                + " engine=" + _sttEngine);

            try
            {
                _sapi?.RecognizeAsyncStop();
            }
            catch (Exception ex)
            {
                AppLog.Warn("STT RecognizeAsyncStop: " + ex.Message);
            }

            try { _waveIn?.StopRecording(); } catch { /* ignore */ }
            try { _offline?.Stop(); } catch { /* ignore */ }
            StopScoWake();

            // brief wait for final segment / RecognizeCompleted
            Monitor.Wait(_gate, _sttEngine != SttEnginePicker.Sapi ? 1500 : 400);
            Monitor.PulseAll(_gate);

            var text = _final.ToString().Trim();
            if (text.Length == 0 && !string.IsNullOrWhiteSpace(_lastHypothesis))
            {
                AppLog.Warn("STT Stop: discarding unconfirmed hypothesis chars="
                    + _lastHypothesis.Length + " text=" + Truncate(_lastHypothesis, 120));
            }

            StopCore_NoLock(clearText: false);
            _final.Clear();
            _lastHypothesis = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                StatusChanged?.Invoke("Пустая запись");
                AppLog.Warn("STT Stop result empty");
            }
            else
            {
                StatusChanged?.Invoke("Готово");
                AppLog.Info("STT Stop result chars=" + text.Length + " text=" + Truncate(text, 160));
            }

            return text;
        }
    }

    public void Cancel()
    {
        SpeechRecognitionEngine? engine;
        WaveInEvent? wave;
        OfflineSttRecognizer? offline;
        lock (_gate)
        {
            AppLog.Info("STT Cancel");
            _listening = false;
            engine = _sapi;
            _sapi = null;
            wave = _waveIn;
            _waveIn = null;
            offline = _offline;
            _offline = null;
            StopScoWake();
            if (engine != null)
            {
                try
                {
                    engine.SpeechHypothesized -= OnHypothesized;
                    engine.SpeechRecognized -= OnRecognized;
                    engine.SpeechRecognitionRejected -= OnRejected;
                    engine.RecognizeCompleted -= OnRecognizeCompleted;
                    engine.RecognizeAsyncCancel();
                }
                catch { /* ignore */ }
            }

            try { _mmEnum?.Dispose(); } catch { /* ignore */ }
            _mmEnum = null;
            _final.Clear();
            _lastHypothesis = string.Empty;
            StatusChanged?.Invoke(string.Empty);
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (wave != null)
                {
                    try { wave.DataAvailable -= OnWaveData; } catch { /* ignore */ }
                    try { wave.StopRecording(); } catch { /* ignore */ }
                    wave.Dispose();
                }
            }
            catch (Exception ex) { AppLog.Warn("STT Cancel wave: " + ex.Message); }

            try { offline?.Dispose(); }
            catch (Exception ex) { AppLog.Warn("STT Cancel offline: " + ex.Message); }

            try { engine?.Dispose(); }
            catch (Exception ex) { AppLog.Warn("STT Cancel dispose: " + ex.Message); }
        });
    }

    private void OnHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        var t = (e.Result?.Text ?? string.Empty).Trim();
        if (t.Length == 0) return;
        Interlocked.Increment(ref _hypEvents);
        lock (_gate)
        {
            _lastHypothesis = t;
            var display = BuildDisplayText();
            if (_hypEvents <= 3 || _hypEvents % 10 == 0)
                AppLog.Info("STT hyp #" + _hypEvents + ": " + Truncate(t, 80));
            try
            {
                PartialResult?.Invoke(display);
                StatusChanged?.Invoke("🎤 " + display);
            }
            catch (Exception ex)
            {
                AppLog.Warn("STT PartialResult: " + ex.Message);
            }
        }
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var t = (e.Result?.Text ?? string.Empty).Trim();
        var conf = e.Result?.Confidence ?? 0;
        Interlocked.Increment(ref _recEvents);

        if (e.Result == null || conf < SpeechRecognizerPicker.MinConfidence || t.Length == 0)
        {
            AppLog.Info("STT Recognized below threshold #" + _recEvents
                + " conf=" + conf.ToString("0.00")
                + " min=" + SpeechRecognizerPicker.MinConfidence.ToString("0.00")
                + " text=" + Truncate(t, 120));
            return;
        }

        AppLog.Info("STT Recognized #" + _recEvents + " conf=" + conf.ToString("0.00") + " text=" + Truncate(t, 120));
        lock (_gate)
        {
            if (_final.Length > 0) _final.Append(' ');
            _final.Append(t);
            _lastHypothesis = string.Empty;
            var display = BuildDisplayText();
            try
            {
                SegmentRecognized?.Invoke(t);
                PartialResult?.Invoke(display);
                StatusChanged?.Invoke("🎤 " + display);
            }
            catch (Exception ex)
            {
                AppLog.Warn("STT PartialResult: " + ex.Message);
            }
        }
    }

    private void OnRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        Interlocked.Increment(ref _rejectEvents);
        var t = e.Result?.Text ?? "";
        AppLog.Warn("STT Rejected #" + _rejectEvents + " conf=" + (e.Result?.Confidence.ToString("0.00") ?? "?")
            + " text=" + Truncate(t, 80));
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
            if (!_listening || _sapi == null || e.Cancelled)
                return;

            if (e.Error != null)
            {
                _restartCount++;
                AppLog.Warn("STT completed error (" + _restartCount + "/2): " + e.Error.Message);
                if (_restartCount > 2)
                {
                    AppLog.Error("STT stopped — repeated Internal error (no usable mic input)");
                    StatusChanged?.Invoke("Микрофон недоступен");
                    _listening = false;
                    return;
                }
            }

            try
            {
                if (e.Error != null)
                {
                    try { _sapi.SetInputToDefaultAudioDevice(); }
                    catch (Exception rebindEx)
                    {
                        AppLog.Error("STT rebind failed: " + rebindEx.Message);
                        _listening = false;
                        StatusChanged?.Invoke("Микрофон недоступен");
                        return;
                    }
                }

                _sapi.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex)
            {
                AppLog.Error("STT restart failed: " + ex.Message);
                _listening = false;
            }
        }
    }

    private string BuildDisplayText()
    {
        if (_final.Length == 0) return _lastHypothesis;
        if (string.IsNullOrEmpty(_lastHypothesis)) return _final.ToString();
        return _final + " " + _lastHypothesis;
    }

    private void StopCore_NoLock(bool clearText)
    {
        _listening = false;
        var engine = _sapi;
        _sapi = null;
        var wave = _waveIn;
        _waveIn = null;
        var offline = _offline;
        _offline = null;
        StopScoWake();

        if (engine != null)
        {
            try
            {
                engine.SpeechHypothesized -= OnHypothesized;
                engine.SpeechRecognized -= OnRecognized;
                engine.SpeechRecognitionRejected -= OnRejected;
                engine.RecognizeCompleted -= OnRecognizeCompleted;
                try { engine.RecognizeAsyncCancel(); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                AppLog.Warn("STT engine cancel: " + ex.Message);
            }
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (wave != null)
                {
                    try { wave.DataAvailable -= OnWaveData; } catch { /* ignore */ }
                    try { wave.StopRecording(); } catch { /* ignore */ }
                    wave.Dispose();
                }
            }
            catch (Exception ex) { AppLog.Warn("STT wave dispose: " + ex.Message); }

            try { offline?.Dispose(); }
            catch (Exception ex) { AppLog.Warn("STT offline dispose: " + ex.Message); }

            try { engine?.Dispose(); }
            catch (Exception ex) { AppLog.Warn("STT engine dispose: " + ex.Message); }
        });

        try { _mmEnum?.Dispose(); } catch { /* ignore */ }
        _mmEnum = null;
        if (clearText)
        {
            _final.Clear();
            _lastHypothesis = string.Empty;
        }
    }

    private static MMDevice? PickHandsFree(MMDeviceEnumerator enumerator, string? preferredHeadsetName)
    {
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged)
            .ToList();

        static bool IsHf(string? n) =>
            n != null && n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0;
        static bool IsVirt(string? n) =>
            n != null && (n.IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) >= 0
                          || n.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0);

        int Score(string name)
        {
            if (string.IsNullOrWhiteSpace(preferredHeadsetName)) return IsHf(name) ? 1 : 0;
            var score = 0;
            if (IsHf(name)) score += 5;
            foreach (var t in preferredHeadsetName!.Split(new[] { ' ', '\'', '#', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (t.Length >= 3 && name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 2;
            }
            return score;
        }

        var best = devices
            .Where(d => !IsVirt(d.FriendlyName))
            .Select(d => new { D = d, S = Score(d.FriendlyName ?? "") })
            .Where(x => x.S > 0)
            .OrderByDescending(x => x.S)
            .ThenBy(x => x.D.State == DeviceState.Active ? 0 : 1)
            .FirstOrDefault();
        return best?.D;
    }

    private static float SafePeak(MMDevice d)
    {
        try { return d.AudioMeterInformation.MasterPeakValue; }
        catch { return 0; }
    }

    private static string ShortName(string name)
    {
        var idx = name.IndexOf('(');
        var s = idx > 0 ? name.Substring(idx).Trim('(', ')', ' ') : name;
        return s.Length > 28 ? s.Substring(0, 28) + "…" : s;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");

    public void Dispose() => Cancel();
}
