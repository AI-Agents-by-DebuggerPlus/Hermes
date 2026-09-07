using System;
using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Buffers PCM and runs recognition on short segments.
/// Prefers Google STT, then Azure, then System.Speech (SAPI).
/// </summary>
public sealed class OfflineSttRecognizer : IDisposable
{
    private const int CloudTargetRate = 16000;

    private readonly object _lock = new();
    private MemoryStream _segmentBuffer = new();
    private readonly int _sampleRate;
    private readonly int _bitsPerSample;
    private readonly int _channels;
    private readonly TimeSpan _segmentLength;
    private readonly float _minConfidence;
    private readonly AppSettings? _settings;
    private readonly GoogleSpeechSttClient _google = new();
    private readonly AzureSpeechSttClient _azure = new();
    private CancellationTokenSource? _cts;
    private Task? _flushLoopTask;
    private string _cultureName = "ru-RU";
    private string _engine = SttEnginePicker.Sapi;
    private bool _disposed;

    public event Action<string>? SegmentRecognized;
    public event Action<string>? SegmentRejected;
    public event Action<Exception>? RecognitionError;

    public OfflineSttRecognizer(
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        TimeSpan? segmentLength = null,
        float minConfidence = SpeechRecognizerPicker.MinConfidence,
        AppSettings? settings = null)
    {
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample;
        _channels = channels;
        _segmentLength = segmentLength ?? TimeSpan.FromSeconds(1.5);
        _minConfidence = minConfidence;
        _settings = settings;
    }

    public string EngineName => _engine;
    public bool UsingCloud =>
        _engine == SttEnginePicker.Google || _engine == SttEnginePicker.Azure;

    public void WritePcm(byte[] buffer, int offset, int count)
    {
        if (_disposed || count <= 0) return;
        lock (_lock)
        {
            _segmentBuffer.Write(buffer, offset, count);
        }
    }

    public void WritePcm(byte[] buffer, int count) => WritePcm(buffer, 0, count);

    public void Start(string cultureName = "ru-RU")
    {
        _cultureName = string.IsNullOrWhiteSpace(cultureName) ? "ru-RU" : cultureName.Trim();
        Stop();
        _cts = new CancellationTokenSource();
        lock (_lock)
        {
            _segmentBuffer.SetLength(0);
        }

        _engine = SttEnginePicker.Pick(_settings);
        if (_engine == SttEnginePicker.Sapi)
            SpeechRecognizerPicker.LogInstalledRecognizersOnce();

        var token = _cts.Token;
        _flushLoopTask = Task.Run(() => FlushLoopAsync(token));
        AppLog.Info("OfflineSttRecognizer Start culture=" + _cultureName
            + " engine=" + _engine
            + " minConf=" + _minConfidence.ToString("0.00", CultureInfo.InvariantCulture)
            + " segmentSec=" + _segmentLength.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
            + " captureHz=" + _sampleRate);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    private async Task FlushLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_segmentLength, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            byte[] segment;
            lock (_lock)
            {
                if (_segmentBuffer.Length == 0)
                    continue;
                segment = _segmentBuffer.ToArray();
                _segmentBuffer.SetLength(0);
            }

            try
            {
                await RecognizeSegmentAsync(segment, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                RecognitionError?.Invoke(ex);
            }
        }

        byte[] leftover;
        lock (_lock)
        {
            leftover = _segmentBuffer.Length > 0 ? _segmentBuffer.ToArray() : Array.Empty<byte>();
            _segmentBuffer.SetLength(0);
        }

        if (leftover.Length > 0)
        {
            try { await RecognizeSegmentAsync(leftover, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { RecognitionError?.Invoke(ex); }
        }
    }

    private async Task RecognizeSegmentAsync(byte[] pcmBytes, CancellationToken ct)
    {
        if (IsEffectivelySilent(pcmBytes))
            return;

        if (_engine == SttEnginePicker.Google && _settings != null)
        {
            await RecognizeGoogleAsync(pcmBytes, ct).ConfigureAwait(false);
            return;
        }

        if (_engine == SttEnginePicker.Azure && _settings != null)
        {
            await RecognizeAzureAsync(pcmBytes, ct).ConfigureAwait(false);
            return;
        }

        RecognizeSapi(pcmBytes);
    }

    private async Task RecognizeGoogleAsync(byte[] pcmBytes, CancellationToken ct)
    {
        if (!TryPrepareCloudPcm(pcmBytes, out var pcm16, out var rate, out var err))
        {
            SegmentRejected?.Invoke(err);
            return;
        }

        var result = await _google.RecognizePcm16Async(_settings!, pcm16, rate, _cultureName, ct)
            .ConfigureAwait(false);
        if (result.Success)
        {
            SegmentRecognized?.Invoke(result.Transcript!.Trim());
            return;
        }

        SegmentRejected?.Invoke("(" + result.Status + ")");
    }

    private async Task RecognizeAzureAsync(byte[] pcmBytes, CancellationToken ct)
    {
        if (!TryPrepareCloudPcm(pcmBytes, out var pcm16, out var rate, out var err))
        {
            SegmentRejected?.Invoke(err);
            return;
        }

        var wav = BuildWavBytes(pcm16, rate, 16, 1);
        var result = await _azure.RecognizeWavAsync(_settings!, wav, _cultureName, rate, ct).ConfigureAwait(false);
        if (result.Success)
        {
            SegmentRecognized?.Invoke(result.DisplayText!.Trim());
            return;
        }

        SegmentRejected?.Invoke("(" + result.Status
            + (string.IsNullOrWhiteSpace(result.DisplayText) ? "" : ", text=" + result.DisplayText) + ")");
    }

    private bool TryPrepareCloudPcm(byte[] pcmBytes, out byte[] pcm16, out int rate, out string err)
    {
        pcm16 = pcmBytes;
        rate = _sampleRate;
        err = string.Empty;
        if (_bitsPerSample != 16 || _channels != 1)
        {
            err = "(cloud STT needs 16-bit mono PCM)";
            return false;
        }

        if (rate != CloudTargetRate)
        {
            pcm16 = PcmResampler.ResamplePcm16Mono(pcmBytes, rate, CloudTargetRate);
            rate = CloudTargetRate;
        }

        return true;
    }

    private void RecognizeSapi(byte[] pcmBytes)
    {
        var tempWavPath = Path.Combine(Path.GetTempPath(), "hermes_stt_" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteWavFile(tempWavPath, pcmBytes, _sampleRate, _bitsPerSample, _channels);

            var info = SpeechRecognizerPicker.Pick(_cultureName);
            if (info == null)
            {
                SegmentRejected?.Invoke("(no recognizer installed)");
                return;
            }

            using var engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToWaveFile(tempWavPath);

            var result = engine.Recognize();
            if (result != null
                && result.Confidence >= _minConfidence
                && !string.IsNullOrWhiteSpace(result.Text))
            {
                SegmentRecognized?.Invoke(result.Text.Trim());
            }
            else
            {
                SegmentRejected?.Invoke(result == null
                    ? "(no match)"
                    : "(below confidence: " + result.Confidence.ToString("0.00", CultureInfo.InvariantCulture)
                      + ", text=" + (result.Text ?? "") + ")");
            }
        }
        finally
        {
            try { File.Delete(tempWavPath); } catch { /* ignore */ }
        }
    }

    private static bool IsEffectivelySilent(byte[] pcm16)
    {
        const short threshold = 400;
        for (var i = 0; i + 1 < pcm16.Length; i += 2)
        {
            var sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            if (Math.Abs((int)sample) > threshold)
                return false;
        }

        return true;
    }

    private static byte[] BuildWavBytes(byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
    {
        using var ms = new MemoryStream(44 + pcmData.Length);
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteWav(bw, pcmData, sampleRate, bitsPerSample, channels);
        }

        return ms.ToArray();
    }

    private static void WriteWavFile(string path, byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        WriteWav(bw, pcmData, sampleRate, bitsPerSample, channels);
    }

    private static void WriteWav(BinaryWriter bw, byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
    {
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = (short)(channels * (bitsPerSample / 8));

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcmData.Length);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(pcmData.Length);
        bw.Write(pcmData);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
        lock (_lock)
        {
            try { _segmentBuffer.Dispose(); } catch { /* ignore */ }
            _segmentBuffer = new MemoryStream();
        }
    }
}
