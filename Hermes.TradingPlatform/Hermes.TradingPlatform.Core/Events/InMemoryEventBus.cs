namespace Hermes.TradingPlatform.Core.Events;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly object _sync = new();
    private readonly Dictionary<Type, List<Delegate>> _typedHandlers = new();
    private readonly List<Action<IPlatformEvent>> _globalHandlers = [];

    /// <summary>Optional sink for handler errors. Defaults to writing to Trace; never null after construction.</summary>
    public Action<Exception, string> OnHandlerError { get; set; } =
        (ex, ctx) => System.Diagnostics.Trace.WriteLine($"[EventBus] handler error in {ctx}: {ex}");

    public void Subscribe<T>(Action<T> handler) where T : class, IPlatformEvent
    {
        lock (_sync)
        {
            var key = typeof(T);
            if (!_typedHandlers.TryGetValue(key, out var list))
            {
                list = [];
                _typedHandlers[key] = list;
            }

            list.Add(handler);
        }
    }

    public void SubscribeAll(Action<IPlatformEvent> handler)
    {
        lock (_sync)
        {
            _globalHandlers.Add(handler);
        }
    }

    public void Publish<T>(T platformEvent) where T : class, IPlatformEvent
    {
        Action<T>[] typed;
        Action<IPlatformEvent>[] global;
        lock (_sync)
        {
            typed = _typedHandlers.TryGetValue(typeof(T), out var list)
                ? list.Cast<Action<T>>().ToArray()
                : [];
            global = _globalHandlers.ToArray();
        }

        // Subscribers are isolated: a throwing handler must not break the publisher
        // or block sibling subscribers from receiving the event.
        foreach (var handler in typed)
        {
            try
            {
                handler(platformEvent);
            }
            catch (Exception ex)
            {
                OnHandlerError(ex, $"typed<{typeof(T).Name}>");
            }
        }

        foreach (var handler in global)
        {
            try
            {
                handler(platformEvent);
            }
            catch (Exception ex)
            {
                OnHandlerError(ex, $"global<{typeof(T).Name}>");
            }
        }
    }
}
