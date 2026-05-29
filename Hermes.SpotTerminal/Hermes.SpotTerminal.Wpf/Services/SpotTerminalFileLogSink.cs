using Hermes.SpotTerminal.Core.Events;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.SpotTerminal.Wpf.Services;

/// <summary>Subscribes platform events to the session log file.</summary>
public sealed class SpotTerminalFileLogSink
{
    public SpotTerminalFileLogSink(SpotTerminalHost host)
    {
        var log = SpotTerminalFileLogger.Instance;
        log.Info($"log file: {log.SessionPath}");
        log.Info($"data root: {SpotBridgePaths.DataRoot}");
        log.Info($"bridge: {SpotBridgePaths.BridgeRoot}");
        log.Info($"unified snapshot: {Hermes.Terminals.Shared.Bridge.UnifiedBridgePaths.UnifiedSnapshotFile}");

        host.EventBus.Subscribe<PlatformLogEvent>(e => log.Platform(e.Entry));
        host.EventBus.Subscribe<AgentEventRecorded>(e =>
            log.Info($"[Agent/{e.Event.Kind}] {e.Event.Summary} symbol={e.Event.Symbol}"));
        host.EventBus.Subscribe<OrderPlacedEvent>(e =>
            log.Exchange($"placed {e.Order.Symbol} {e.Order.Side} {e.Order.Type} qty={e.Order.Quantity} id={e.Order.Id} status={e.Order.Status}"));
        host.EventBus.Subscribe<OrderFilledEvent>(e =>
            log.Exchange($"filled {e.Order.Symbol} id={e.Order.Id} qty={e.Order.Quantity}"));
        host.EventBus.Subscribe<OrderCancelledEvent>(e =>
            log.Exchange($"cancelled orderId={e.OrderId}"));

        log.Info($"Spot Terminal ready · mode={host.ExecutionMode} feed={host.FeedStatusLabel}");
    }
}
