// Reference implementation. Adapt namespace/usings to match the actual project structure —
// this is not a drop-in file, it's the target shape for a new class that replaces the
// live SetInputToAudioStream(pump) path with buffered offline recognition.
//
// Rationale (from three diagnostic reports):
//   - PcmPumpStream.Length/Position fix stopped the startup crash.
//   - Removing silence padding gave SAPI a "clean" live stream, but it still never fired
//     SpeechHypothesized/SpeechRecognized even with peak up to 0.522 — System.Speech's live
//     dictation path appears fundamentally unreliable over this BT capture path.
//   - Rather than patching the live-stream path a fourth time, switch to: capture N seconds
//     of PCM -> write a temp WAV -> SpeechRecognitionEngine.SetInputToWaveFile -> Recognize()
//     (single-shot, offline, no live endpointing needed).

using System;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes.EnglishTutorClient.Services
{
    /// <summary>
    /// Buffers raw 16kHz mono PCM float/int16 audio and runs offline (non-live)
    /// System.Speech recognition against short WAV segments, instead of streaming
    /// directly into SpeechRecognitionEngine via a live Stream.
    /// </summary>
    public sealed class OfflineSttRecognizer : IDisposable
    {
        private readonly object _lock = new object();
        private MemoryStream _segmentBuffer = new MemoryStream();
        private readonly int _sampleRate;
        private readonly int _bitsPerSample;
        private readonly int _channels;
        private readonly TimeSpan _segmentLength;
        private CancellationTokenSource _cts;
        private Task _flushLoopTask;

        public event Action<string> SegmentRecognized;      // final text for a segment
        public event Action<string> SegmentRejected;        // segment had audio but no confident match
        public event Action<Exception> RecognitionError;

        public OfflineSttRecognizer(
            int sampleRate = 16000,
            int bitsPerSample = 16,
            int channels = 1,
            TimeSpan? segmentLength = null)
        {
            _sampleRate = sampleRate;
            _bitsPerSample = bitsPerSample;
            _channels = channels;
            _segmentLength = segmentLength ?? TimeSpan.FromSeconds(1.5);
        }

        /// <summary>
        /// Call this from the existing WASAPI capture callback (OnCaptureData) instead of
        /// PcmPumpStream.WritePcm. Same signature shape, so the call site swap is minimal.
        /// </summary>
        public void WritePcm(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                _segmentBuffer.Write(buffer, offset, count);
            }
        }

        public void Start(string cultureName = "en-US")
        {
            _cultureName = cultureName;
            _cts = new CancellationTokenSource();
            _flushLoopTask = Task.Run(() => FlushLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Non-blocking stop — mirrors the non-blocking StopStt fix from the previous pass.
        /// Do not Wait() on the flush task here; let it observe cancellation and exit on its
        /// own background thread.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        private string _cultureName = "en-US";

        private async Task FlushLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_segmentLength, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                byte[] segment;
                lock (_lock)
                {
                    if (_segmentBuffer.Length == 0)
                    {
                        continue;
                    }
                    segment = _segmentBuffer.ToArray();
                    _segmentBuffer.SetLength(0);
                }

                try
                {
                    RecognizeSegment(segment);
                }
                catch (Exception ex)
                {
                    RecognitionError?.Invoke(ex);
                }
            }
        }

        private void RecognizeSegment(byte[] pcmBytes)
        {
            // Skip near-silent segments to save CPU / avoid pointless recognizer calls —
            // reuse whatever peak-detection helper already exists in OnCaptureData if one
            // is available; a simple RMS/max-abs check is fine here as a fallback.
            if (IsEffectivelySilent(pcmBytes))
            {
                return;
            }

            var tempWavPath = Path.Combine(Path.GetTempPath(), $"hermes_stt_{Guid.NewGuid():N}.wav");
            try
            {
                WriteWavFile(tempWavPath, pcmBytes, _sampleRate, _bitsPerSample, _channels);

                using var engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(_cultureName));
                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToWaveFile(tempWavPath);

                var result = engine.Recognize(); // single-shot, offline — no live endpointing
                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                {
                    SegmentRecognized?.Invoke(result.Text);
                }
                else
                {
                    SegmentRejected?.Invoke("(no confident match)");
                }
            }
            finally
            {
                try { File.Delete(tempWavPath); } catch { /* best-effort cleanup */ }
            }
        }

        private static bool IsEffectivelySilent(byte[] pcm16)
        {
            // Simple max-abs-sample check for 16-bit PCM. Replace with the project's existing
            // peak helper if one is already shared with OnCaptureData, to keep thresholds consistent.
            const short threshold = 400; // ~ -36 dBFS-ish; tune against real captured segments
            for (int i = 0; i + 1 < pcm16.Length; i += 2)
            {
                short sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
                if (Math.Abs((int)sample) > threshold)
                {
                    return false;
                }
            }
            return true;
        }

        private static void WriteWavFile(string path, byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));

            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + pcmData.Length);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write((short)bitsPerSample);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(pcmData.Length);
            bw.Write(pcmData);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
