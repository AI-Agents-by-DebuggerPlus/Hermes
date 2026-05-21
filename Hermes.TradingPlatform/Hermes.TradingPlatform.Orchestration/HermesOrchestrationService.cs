using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Orchestration;

/// <summary>
/// Rule-based orchestration layer (no AI, no order execution). Phase 6 MVP.
/// </summary>
public sealed class HermesOrchestrationService : IHermesOrchestrator
{
    private const int MaxDecisions = 50;
    private const int MaxTasks = 20;

    private readonly IEventBus _bus;
    private readonly ITradingStateStore _store;
    private DateTimeOffset _lastReasoningRefresh = DateTimeOffset.MinValue;
    private bool _enabled = true;

    public HermesOrchestrationService(IEventBus bus, ITradingStateStore store)
    {
        _bus = bus;
        _store = store;
        _bus.Subscribe<MarketTickEvent>(OnMarketTick);
        _bus.Subscribe<StrategySignalEvent>(OnStrategySignal);
        _bus.Subscribe<RiskTriggeredEvent>(OnRiskTriggered);
        _bus.Subscribe<OrderFilledEvent>(OnOrderFilled);
        _bus.Subscribe<OrderPlacedEvent>(OnOrderPlaced);
    }

    public bool IsEnabled => _enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            RefreshReasoning(_store.Snapshot);
        }
    }

    private void OnMarketTick(MarketTickEvent tick)
    {
        if (!_enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastReasoningRefresh < TimeSpan.FromSeconds(20))
        {
            return;
        }

        _lastReasoningRefresh = now;
        RefreshReasoning(_store.Snapshot);
    }

    private void OnStrategySignal(StrategySignalEvent signal)
    {
        if (!_enabled)
        {
            return;
        }

        var execNote = signal.AutoExecuteRequested
            ? "Platform auto-exec via StrategyRunner (Hermes did not place the order)."
            : "Signal only — no auto-exec.";

        AddDecision(
            $"Review [{signal.StrategyName}]: {signal.Side} {signal.Symbol} {signal.OrderType} — {signal.Reason}. {execNote}");

        _store.Mutate(s =>
        {
            s.Hermes.State = HermesOrchestrationState.Reviewing;
            s.Hermes.ActiveStrategy = signal.StrategyName;
        });

        RefreshReasoning(_store.Snapshot);
    }

    private void OnRiskTriggered(RiskTriggeredEvent risk)
    {
        if (!_enabled)
        {
            return;
        }

        _store.Mutate(s =>
        {
            s.Hermes.State = HermesOrchestrationState.Halted;
            s.Hermes.Mode = "Halted — operator action required";
        });

        AddDecision($"Risk event: {risk.Reason}. Hermes halted orchestration; no order bypass.");
        UpsertTask("Review emergency halt", "Urgent");
    }

    private void OnOrderFilled(OrderFilledEvent filled)
    {
        if (!_enabled)
        {
            return;
        }

        AddDecision(
            $"Post-trade review: {filled.Order.Id} {filled.Order.Symbol} filled @ {filled.FillPrice:N2}. " +
            "Hermes observation only — no execution authority.");
    }

    private void OnOrderPlaced(OrderPlacedEvent placed)
    {
        if (!_enabled)
        {
            return;
        }

        if (placed.Order.Status == OrderStatus.Rejected)
        {
            AddDecision(
                $"Order rejected: {placed.Order.Id} {placed.Order.Symbol}. Risk gate blocked entry (expected under Safe Mode / limits).");
        }
    }

    private void RefreshReasoning(TradingPlatformState snapshot)
    {
        var running = snapshot.Strategies
            .Where(s => s is { IsEnabled: true, Status: StrategyRunStatus.Running })
            .Select(s => s.Name)
            .ToList();

        var active = running.FirstOrDefault() ?? "—";
        var positionSummary = snapshot.Positions.Count == 0
            ? "flat"
            : string.Join(", ", snapshot.Positions.Select(p => $"{p.Symbol} {p.Side}"));

        var reasoning =
            $"Orchestration monitor: {snapshot.Risk.RiskLevel} risk, DD {snapshot.Risk.DailyDrawdownPercent:F1}%, " +
            $"exposure {snapshot.Risk.ExposurePercent:F1}%. Positions: {positionSummary}. " +
            $"Running strategies: {(running.Count > 0 ? string.Join(", ", running) : "none")}. " +
            "Hermes does not execute orders — flow is StrategyRunner → VirtualExchange → RiskValidator.";

        if (snapshot.Risk.EmergencyHalt)
        {
            reasoning = "EMERGENCY HALT active. Hermes suspended recommendations until operator clears halt.";
        }
        else if (snapshot.Risk.SafeMode)
        {
            reasoning += " Safe Mode: new entries must be reduce-only.";
        }

        var context =
            $"Active focus: {active}. Watchlist: {string.Join(", ", snapshot.Tickers.Where(t => t.InWatchlist).Select(t => t.Symbol))}. " +
            $"Equity {snapshot.Account.Equity:N2}.";

        var confidence = snapshot.Risk.EmergencyHalt
            ? 0.1m
            : Math.Clamp(1m - snapshot.Risk.DailyDrawdownPercent / 20m, 0.25m, 0.95m);

        var state = snapshot.Risk.EmergencyHalt
            ? HermesOrchestrationState.Halted
            : running.Count > 0
                ? HermesOrchestrationState.Monitoring
                : HermesOrchestrationState.Monitoring;

        _store.Mutate(s =>
        {
            s.Hermes.State = state;
            s.Hermes.ActiveStrategy = active;
            s.Hermes.Confidence = confidence;
            s.Hermes.Mode = snapshot.Risk.EmergencyHalt
                ? "Halted"
                : "Orchestration / Paper (no direct execution)";
            s.Hermes.CurrentReasoning = reasoning;
            s.Hermes.StrategyContext = context;
        });
    }

    private void AddDecision(string summary)
    {
        _store.Mutate(s =>
        {
            s.Hermes.Decisions.Insert(0, new HermesDecision
            {
                Timestamp = DateTimeOffset.UtcNow,
                Summary = summary,
            });

            if (s.Hermes.Decisions.Count > MaxDecisions)
            {
                s.Hermes.Decisions.RemoveRange(MaxDecisions, s.Hermes.Decisions.Count - MaxDecisions);
            }

            if (s.Hermes.State != HermesOrchestrationState.Halted)
            {
                s.Hermes.State = HermesOrchestrationState.Reviewing;
            }
        });

        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Hermes",
            Source = "Orchestrator",
            Message = summary.Length > 120 ? summary[..120] + "…" : summary,
        }));
    }

    private void UpsertTask(string title, string status)
    {
        _store.Mutate(s =>
        {
            var existing = s.Hermes.Tasks.FirstOrDefault(t => t.Title == title);
            if (existing is null)
            {
                s.Hermes.Tasks.Insert(0, new HermesTask { Title = title, Status = status });
            }

            if (s.Hermes.Tasks.Count > MaxTasks)
            {
                s.Hermes.Tasks.RemoveRange(MaxTasks, s.Hermes.Tasks.Count - MaxTasks);
            }
        });
    }
}
