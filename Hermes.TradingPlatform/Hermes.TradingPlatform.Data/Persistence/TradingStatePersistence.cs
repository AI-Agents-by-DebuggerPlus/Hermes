using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>Auto-saves paper session (balance, positions, journal) on state changes and fills.</summary>
public sealed class TradingStatePersistence : IDisposable
{
    private readonly ITradingStateStore _store;
    private readonly TradingSessionStateFileStore _fileStore;
    private readonly Func<int> _getNextOrderSequence;
    private readonly object _sync = new();
    private Timer? _debounceTimer;
    private bool _disposed;

    public TradingStatePersistence(
        ITradingStateStore store,
        IEventBus bus,
        TradingSessionStateFileStore fileStore,
        Func<int> getNextOrderSequence)
    {
        _store = store;
        _fileStore = fileStore;
        _getNextOrderSequence = getNextOrderSequence;
        _store.StateChanged += (_, _) => ScheduleSave();
        bus.Subscribe<OrderFilledEvent>(_ => SaveNow());
        bus.Subscribe<OrderPlacedEvent>(_ => ScheduleSave());
        bus.Subscribe<OrderCancelledEvent>(_ => ScheduleSave());
    }

    private void ScheduleSave()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => SaveNow(), null, 400, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _fileStore.Save(_store.Snapshot, _getNextOrderSequence());
        }
        catch
        {
            // non-fatal — next tick will retry
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        SaveNow();
    }
}
