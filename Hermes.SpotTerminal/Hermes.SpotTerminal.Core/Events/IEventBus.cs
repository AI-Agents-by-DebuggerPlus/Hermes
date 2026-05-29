namespace Hermes.SpotTerminal.Core.Events;

public interface IEventBus
{
    void Subscribe<T>(Action<T> handler) where T : class, IPlatformEvent;
    void SubscribeAll(Action<IPlatformEvent> handler);
    void Publish<T>(T platformEvent) where T : class, IPlatformEvent;
}
