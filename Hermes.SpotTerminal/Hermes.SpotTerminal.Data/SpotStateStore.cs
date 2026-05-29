using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Data;

public sealed class SpotStateStore : ISpotStateStore
{
    private readonly object _sync = new();
    private SpotPlatformState _state = new();

    public SpotPlatformState Snapshot
    {
        get
        {
            lock (_sync)
            {
                return Clone(_state);
            }
        }
    }

    public event EventHandler? StateChanged;

    public void Initialize(SpotPlatformState state) => Replace(state);

    public void Mutate(Action<SpotPlatformState> update)
    {
        lock (_sync)
        {
            update(_state);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Replace(SpotPlatformState seeded)
    {
        lock (_sync)
        {
            _state = seeded;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SpotPlatformState Clone(SpotPlatformState s)
    {
        var c = new SpotPlatformState
        {
            Mode = s.Mode,
            FeedStatus = s.FeedStatus,
            Agent = new AgentSession
            {
                Id = s.Agent.Id,
                State = s.Agent.State,
                ActiveSkillId = s.Agent.ActiveSkillId,
                CurrentThought = s.Agent.CurrentThought,
                StartedAtUtc = s.Agent.StartedAtUtc,
                LastEventAtUtc = s.Agent.LastEventAtUtc,
            },
        };

        c.Balances.AddRange(s.Balances.Select(b => new SpotBalance { Asset = b.Asset, Free = b.Free, Locked = b.Locked }));
        c.Orders.AddRange(s.Orders.Select(o => new SpotOrder
        {
            Id = o.Id, Symbol = o.Symbol, Type = o.Type, Side = o.Side,
            Price = o.Price, Quantity = o.Quantity, Status = o.Status, CreatedAt = o.CreatedAt,
        }));
        c.Tickers.AddRange(s.Tickers.Select(t => new MarketTicker
        {
            Symbol = t.Symbol, Price = t.Price, ChangePercent24h = t.ChangePercent24h,
            Volume24h = t.Volume24h, InWatchlist = t.InWatchlist,
        }));
        c.Skills.AddRange(s.Skills.Select(sk => new Skill
        {
            Id = sk.Id, Name = sk.Name, Description = sk.Description, Status = sk.Status,
            IsInitial = sk.IsInitial, ParametersJson = sk.ParametersJson,
            CreatedAtUtc = sk.CreatedAtUtc, ApprovedAtUtc = sk.ApprovedAtUtc,
            LastBacktest = sk.LastBacktest is null ? null : new BacktestSummary
            {
                RunAtUtc = sk.LastBacktest.RunAtUtc, Trades = sk.LastBacktest.Trades,
                NetPnl = sk.LastBacktest.NetPnl, MaxDrawdownPercent = sk.LastBacktest.MaxDrawdownPercent,
                PassedThreshold = sk.LastBacktest.PassedThreshold,
            },
        }));
        c.Logs.AddRange(s.Logs.Select(l => new PlatformLogEntry
        {
            Timestamp = l.Timestamp, EventType = l.EventType, Source = l.Source, Message = l.Message,
        }));
        c.AgentEvents.AddRange(s.AgentEvents.Select(e => new AgentEvent
        {
            Id = e.Id, TimestampUtc = e.TimestampUtc, Kind = e.Kind, SessionId = e.SessionId,
            Symbol = e.Symbol, Summary = e.Summary, PayloadJson = e.PayloadJson,
        }));
        c.LearningJournal.AddRange(s.LearningJournal.Select(j => new LearningJournalEntry
        {
            Id = j.Id, TimestampUtc = j.TimestampUtc, Category = j.Category, Title = j.Title,
            Body = j.Body, Tags = [.. j.Tags], RelatedSkillId = j.RelatedSkillId, Symbol = j.Symbol,
        }));
        return c;
    }
}
