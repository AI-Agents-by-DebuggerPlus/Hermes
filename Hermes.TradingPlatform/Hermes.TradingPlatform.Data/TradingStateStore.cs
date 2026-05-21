using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data;

public sealed class TradingStateStore : ITradingStateStore
{
    private readonly object _sync = new();
    private TradingPlatformState _state = new();

    public TradingPlatformState Snapshot
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

    public void Mutate(Action<TradingPlatformState> update)
    {
        lock (_sync)
        {
            update(_state);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Initialize(TradingPlatformState seeded) => Replace(seeded);

    public void Replace(TradingPlatformState seeded)
    {
        lock (_sync)
        {
            _state = seeded;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TradingPlatformState Clone(TradingPlatformState source)
    {
        var clone = new TradingPlatformState
        {
            Account = new TradingAccount
            {
                Balance = source.Account.Balance,
                Equity = source.Account.Equity,
                FreeMargin = source.Account.FreeMargin,
                UsedMargin = source.Account.UsedMargin,
                Leverage = source.Account.Leverage,
            },
            Pnl = new PnlTracker
            {
                Today = source.Pnl.Today,
                Week = source.Pnl.Week,
                Month = source.Pnl.Month,
                AllTime = source.Pnl.AllTime,
            },
            Risk = new RiskProfile
            {
                MaxDailyLossPercent = source.Risk.MaxDailyLossPercent,
                MaxPositionSizeBtc = source.Risk.MaxPositionSizeBtc,
                MaxLeverage = source.Risk.MaxLeverage,
                MaxExposurePercent = source.Risk.MaxExposurePercent,
                SafeMode = source.Risk.SafeMode,
                AutoShutdown = source.Risk.AutoShutdown,
                EmergencyHalt = source.Risk.EmergencyHalt,
                DailyDrawdownPercent = source.Risk.DailyDrawdownPercent,
                ExposurePercent = source.Risk.ExposurePercent,
                RiskLevel = source.Risk.RiskLevel,
            },
            Hermes = new HermesState
            {
                State = source.Hermes.State,
                ActiveStrategy = source.Hermes.ActiveStrategy,
                Confidence = source.Hermes.Confidence,
                Mode = source.Hermes.Mode,
                CurrentReasoning = source.Hermes.CurrentReasoning,
                StrategyContext = source.Hermes.StrategyContext,
            },
        };

        clone.Hermes.Tasks.AddRange(source.Hermes.Tasks.Select(t => new HermesTask
        {
            Title = t.Title,
            Status = t.Status,
        }));

        clone.Hermes.Decisions.AddRange(source.Hermes.Decisions.Select(d => new HermesDecision
        {
            Timestamp = d.Timestamp,
            Summary = d.Summary,
        }));

        clone.Positions.AddRange(source.Positions.Select(p => new Position
        {
            Symbol = p.Symbol,
            Side = p.Side,
            Size = p.Size,
            EntryPrice = p.EntryPrice,
            MarkPrice = p.MarkPrice,
            UnrealizedPnl = p.UnrealizedPnl,
            RealizedPnl = p.RealizedPnl,
            LiquidationPrice = p.LiquidationPrice,
        }));

        clone.Orders.AddRange(source.Orders.Select(o => new Order
        {
            Id = o.Id,
            Symbol = o.Symbol,
            Type = o.Type,
            Side = o.Side,
            Price = o.Price,
            TriggerPrice = o.TriggerPrice,
            Quantity = o.Quantity,
            Status = o.Status,
            ReduceOnly = o.ReduceOnly,
            CreatedAt = o.CreatedAt,
        }));

        clone.Tickers.AddRange(source.Tickers.Select(t => new MarketTicker
        {
            Symbol = t.Symbol,
            Price = t.Price,
            ChangePercent24h = t.ChangePercent24h,
            Volume24h = t.Volume24h,
            InWatchlist = t.InWatchlist,
        }));

        clone.Strategies.AddRange(source.Strategies.Select(s => new StrategyState
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            RiskProfileLabel = s.RiskProfileLabel,
            Status = s.Status,
            IsEnabled = s.IsEnabled,
        }));

        clone.Logs.AddRange(source.Logs.Select(l => new PlatformLogEntry
        {
            Timestamp = l.Timestamp,
            EventType = l.EventType,
            Source = l.Source,
            Message = l.Message,
        }));

        clone.Journal.AddRange(source.Journal.Select(j => new TradeJournalEntry
        {
            Id = j.Id,
            Timestamp = j.Timestamp,
            OrderId = j.OrderId,
            Symbol = j.Symbol,
            Kind = j.Kind,
            Side = j.Side,
            Quantity = j.Quantity,
            FillPrice = j.FillPrice,
            Fee = j.Fee,
            RealizedPnl = j.RealizedPnl,
            BalanceBefore = j.BalanceBefore,
            BalanceAfter = j.BalanceAfter,
            ReduceOnly = j.ReduceOnly,
        }));

        return clone;
    }
}
