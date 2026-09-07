using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// Producer/consumer PCM stream: WASAPI writes chunks, SAPI blocks on Read.
/// Length/Position are virtual counters so SpStreamWrapper can construct.
/// Does NOT pad silence — only real WritePcm bytes (synthetic zeros broke SAPI endpointing).
/// Call <see cref="SignalEnd"/> to unblock Read with EOF (0).
/// </summary>
internal sealed class PcmPumpStream : Stream
{
    private readonly BlockingCollection<byte[]> _queue = new(64);
    private readonly ManualResetEventSlim _dataAvailable = new(false);
    private byte[]? _current;
    private int _offset;
    private volatile bool _closed;
    private long _totalBytesWritten;
    private long _totalBytesRead;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length
    {
        get
        {
            var written = Interlocked.Read(ref _totalBytesWritten);
            var read = Interlocked.Read(ref _totalBytesRead);
            return Math.Max(written, read + 1);
        }
    }

    public override long Position
    {
        get => Interlocked.Read(ref _totalBytesRead);
        set => throw new NotSupportedException();
    }

    /// <summary>Unblock any Read waiting in SAPI — returns 0 (EOF), no silence padding.</summary>
    public void SignalEnd()
    {
        if (_closed) return;
        _closed = true;
        try { _queue.CompleteAdding(); } catch { /* ignore */ }
        try { _dataAvailable.Set(); } catch { /* ignore */ }
    }

    public void WritePcm(byte[] data, int count)
    {
        if (_closed || count <= 0) return;
        var copy = new byte[count];
        Buffer.BlockCopy(data, 0, copy, 0, count);
        try
        {
            if (!_queue.TryAdd(copy, 20))
            {
                _queue.TryTake(out _);
                _queue.TryAdd(copy);
            }

            Interlocked.Add(ref _totalBytesWritten, count);
            _dataAvailable.Set();
        }
        catch (InvalidOperationException)
        {
            // completed
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0) return 0;

        var written = 0;
        while (written < count)
        {
            if (_current == null || _offset >= _current.Length)
            {
                _current = null;
                _offset = 0;

                while (true)
                {
                    try
                    {
                        if (_queue.TryTake(out _current, 0))
                        {
                            _offset = 0;
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // CompleteAdding + empty
                        if (written > 0)
                        {
                            Interlocked.Add(ref _totalBytesRead, written);
                            return written;
                        }

                        return 0;
                    }

                    if (_closed)
                    {
                        // Drain finished — clean EOF (do NOT synthesize silence)
                        if (written > 0)
                        {
                            Interlocked.Add(ref _totalBytesRead, written);
                            return written;
                        }

                        return 0;
                    }

                    // Wait for real WASAPI bytes only — no zero-fill padding
                    _dataAvailable.Reset();
                    if (_queue.Count > 0 || _closed)
                        continue;
                    _dataAvailable.Wait(50);
                }
            }

            var n = Math.Min(count - written, _current!.Length - _offset);
            Buffer.BlockCopy(_current, _offset, buffer, offset + written, n);
            _offset += n;
            written += n;
        }

        if (written > 0)
            Interlocked.Add(ref _totalBytesRead, written);
        return written;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count <= 0) return;
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        WritePcm(copy, count);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SignalEnd();
            try
            {
                while (_queue.TryTake(out _)) { }
            }
            catch { /* ignore */ }

            try { _dataAvailable.Dispose(); } catch { /* ignore */ }
        }

        base.Dispose(disposing);
    }
}
