using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Risk;

/// <summary>
/// Watches account/PnL changes and trips the emergency halt when configured
/// risk caps (daily loss, exposure) are exceeded and AutoShutdown is enabled.
/// Subscribes once and lives for the duration of the host.
/// </summary>
public sealed class RiskCircuitBreaker
{
    private readonly ITradingStateStore _store;
    private readonly IEventBus _bus;
    private readonly object _sync = new();
    private bool _alreadyTripped;

    public RiskCircuitBreaker(ITradingStateStore store, IEventBus bus)
    {
        _store = store;
        _bus = bus;
        _store.StateChanged += (_, _) => Evaluate();
        _bus.Subscribe<OrderFilledEvent>(_ => Evaluate());
        _bus.Subscribe<MarketTickEvent>(_ => Evaluate());
    }

    private void Evaluate()
    {
        // Cheap snapshot pre-check to avoid holding the lock too long.
        var snap = _store.Snapshot;
        if (snap.Risk.EmergencyHalt || !snap.Risk.AutoShutdown)
        {
            return;
        }

        var (tripped, reason) = ShouldTrip(snap);
        if (!tripped)
        {
            return;
        }

        lock (_sync)
        {
            if (_alreadyTripped)
            {
                return;
            }

            _alreadyTripped = true;
        }

        _store.Mutate(s =>
        {
            s.Risk.EmergencyHalt = true;
            s.Risk.RiskLevel = RiskLevel.Critical;
            foreach (var strategy in s.Strategies)
            {
                strategy.Status = StrategyRunStatus.Halted;
            }
        });

        _bus.Publish(new RiskTriggeredEvent(reason!, EmergencyHalt: true));
        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Risk",
            Source = "CircuitBreaker",
            Message = $"Auto-shutdown engaged: {reason}",
        }));
    }

    /// <summary>Reset internal flag so the breaker can re-arm after a manual recovery.</summary>
    public void Rearm()
    {
        lock (_sync)
        {
            _alreadyTripped = false;
        }
    }

    private static (bool Trip, string? Reason) ShouldTrip(TradingPlatformState s)
    {
        if (s.Risk.MaxDailyLossPercent > 0 && s.Pnl.Today < 0)
        {
            var startingEquity = s.Account.Balance - s.Pnl.Today;
            if (startingEquity > 0)
            {
                var dd = -s.Pnl.Today / startingEquity * 100m;
                if (dd >= s.Risk.MaxDailyLossPercent)
                {
                    return (true, $"daily loss {dd:F2}% ≥ cap {s.Risk.MaxDailyLossPercent:F2}%");
                }
            }
        }

        if (s.Risk.MaxExposurePercent > 0 && s.Account.Balance > 0)
        {
            var leverage = s.Account.Leverage > 0 ? s.Account.Leverage : 1m;
            var marginUsed = s.Positions.Sum(p => Math.Abs(p.Size * p.MarkPrice)) / leverage;
            var capUsd = s.Account.Balance * (s.Risk.MaxExposurePercent / 100m);
            if (capUsd > 0 && marginUsed > capUsd * 1.05m)
            {
                return (true, $"exposure {marginUsed:N2} > cap {capUsd:N2} ({s.Risk.MaxExposurePercent:F2}%)");
            }
        }

        return (false, null);
    }
}
