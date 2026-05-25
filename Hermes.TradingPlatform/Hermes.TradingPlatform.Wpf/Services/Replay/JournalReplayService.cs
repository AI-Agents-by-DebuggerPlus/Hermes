using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Data.Persistence;

namespace Hermes.TradingPlatform.Wpf.Services.Replay;

/// <summary>
/// Read-only chronological playback of <see cref="TradeJournalEntry"/> records (live state +
/// optional <c>trade_journal.jsonl</c> backup). The service NEVER mutates platform state — it
/// only emits a "current entry" stream that the UI can visualise on the Replay page.
/// </summary>
public sealed class JournalReplayService : IDisposable
{
    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITradingStateStore _store;
    private readonly IJournalStore? _journalStore;
    private readonly DispatcherTimer _tickTimer;
    private List<TradeJournalEntry> _entries = [];
    private int _index = -1;
    private double _speed = 1d;
    private bool _isPlaying;

    public JournalReplayService(ITradingStateStore store, IJournalStore? journalStore = null)
    {
        _store = store;
        _journalStore = journalStore;
        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _tickTimer.Tick += OnTimerTick;
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<TradeJournalEntry> Entries => _entries;

    public int CurrentIndex => _index;

    public TradeJournalEntry? CurrentEntry =>
        _index >= 0 && _index < _entries.Count ? _entries[_index] : null;

    public bool IsPlaying => _isPlaying;

    public double Speed => _speed;

    /// <summary>Reload the timeline from the live state store and (optionally) a journal store backup.</summary>
    public void Reload(string? journalFilePath = null)
    {
        var fromState = _store.Snapshot.Journal.ToList();
        IReadOnlyList<TradeJournalEntry> fromBackup = TryLoadFromStore();
        if (fromBackup.Count == 0 && !string.IsNullOrWhiteSpace(journalFilePath))
        {
            fromBackup = TryLoadFromFile(journalFilePath);
        }

        var combined = fromState.Count >= fromBackup.Count ? fromState : fromBackup;
        _entries = combined
            .OrderBy(e => e.Timestamp)
            .ToList();

        _index = _entries.Count > 0 ? 0 : -1;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<TradeJournalEntry> TryLoadFromStore()
    {
        if (_journalStore is null)
        {
            return [];
        }

        try
        {
            return _journalStore.LoadAll();
        }
        catch
        {
            return [];
        }
    }

    public void Play()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        if (_index < 0 || _index >= _entries.Count)
        {
            _index = 0;
        }

        _isPlaying = true;
        _tickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50d, 1000d / _speed));
        _tickTimer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _isPlaying = false;
        _tickTimer.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSpeed(double speed)
    {
        if (speed <= 0)
        {
            return;
        }

        _speed = speed;
        if (_isPlaying)
        {
            _tickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50d, 1000d / _speed));
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void JumpTo(int index)
    {
        if (_entries.Count == 0)
        {
            _index = -1;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _index = Math.Clamp(index, 0, _entries.Count - 1);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StepForward() => JumpTo(_index + 1);

    public void StepBack() => JumpTo(_index - 1);

    public void Reset()
    {
        Pause();
        _index = _entries.Count > 0 ? 0 : -1;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _entries.Count == 0)
        {
            return;
        }

        if (_index >= _entries.Count - 1)
        {
            Pause();
            return;
        }

        _index++;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static List<TradeJournalEntry> TryLoadFromFile(string? journalFilePath)
    {
        if (string.IsNullOrWhiteSpace(journalFilePath) || !File.Exists(journalFilePath))
        {
            return [];
        }

        try
        {
            var entries = new List<TradeJournalEntry>();
            foreach (var line in File.ReadLines(journalFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<TradeJournalEntry>(line, JournalJsonOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // skip malformed line, keep going
                }
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    public string FormatProgress()
    {
        if (_entries.Count == 0)
        {
            return "no journal data — trade or import to populate";
        }

        var current = CurrentEntry;
        if (current is null)
        {
            return $"0 / {_entries.Count}";
        }

        var ts = current.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{_index + 1} / {_entries.Count} · {ts}";
    }

    public void Dispose()
    {
        _tickTimer.Stop();
        _tickTimer.Tick -= OnTimerTick;
    }
}
