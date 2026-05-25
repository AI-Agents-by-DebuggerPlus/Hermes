using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Mock;
using Hermes.TradingPlatform.Shared.Risk;

namespace Hermes.TradingPlatform.Core.Mapping;

public static class TradingUiMapper
{
    public static AccountSummaryDto ToDto(TradingAccount account) => new()
    {
        Balance = account.Balance,
        Equity = account.Equity,
        FreeMargin = account.FreeMargin,
        UsedMargin = account.UsedMargin,
        Leverage = account.Leverage,
    };

    public static PnlSummaryDto ToDto(PnlTracker pnl) => new()
    {
        Today = pnl.Today,
        Week = pnl.Week,
        Month = pnl.Month,
        AllTime = pnl.AllTime,
    };

    public static PositionDto ToDto(Position position) => ToDto(position, []);

    public static PositionDto ToDto(Position position, IReadOnlyList<Order> orders)
    {
        var exitSide = position.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        decimal? sl = null;
        decimal? tp = null;
        foreach (var o in orders)
        {
            if (o.Status != OrderStatus.Open || !o.ReduceOnly || o.Side != exitSide || o.Symbol != position.Symbol)
            {
                continue;
            }

            if (o.Type == OrderType.Stop && sl is null)
            {
                sl = o.TriggerPrice ?? o.Price;
            }
            else if (o.Type == OrderType.Limit && tp is null)
            {
                tp = o.Price;
            }
        }

        return new PositionDto
        {
            Symbol = position.Symbol,
            Side = position.Side == PositionSide.Long ? "Long" : "Short",
            Size = position.Size,
            EntryPrice = position.EntryPrice,
            MarkPrice = position.MarkPrice,
            UnrealizedPnl = position.UnrealizedPnl,
            RealizedPnl = position.RealizedPnl,
            LiquidationPrice = position.LiquidationPrice,
            StopLossPrice = sl,
            TakeProfitPrice = tp,
        };
    }

    public static OrderDto ToDto(Order order)
    {
        var purpose = order.ReduceOnly
            ? order.Type switch
            {
                OrderType.Stop => "SL",
                OrderType.Limit => "TP",
                _ => "Reduce",
            }
            : "Entry";

        return new OrderDto
        {
            Id = order.Id,
            Symbol = order.Symbol,
            Type = order.Type.ToString(),
            Side = order.Side == OrderSide.Buy ? "Buy" : "Sell",
            Price = order.Price,
            Quantity = order.Quantity,
            Status = order.Status.ToString(),
            ReduceOnly = order.ReduceOnly,
            Purpose = purpose,
        };
    }

    public static RiskProfileSettingsDto ToSettingsDto(RiskProfile risk) => new()
    {
        MaxDailyLossPercent = risk.MaxDailyLossPercent,
        MaxRiskPerTradePercent = risk.MaxRiskPerTradePercent,
        MaxPositionSizeBtc = risk.MaxPositionSizeBtc,
        MaxLeverage = risk.MaxLeverage,
        MaxExposurePercent = risk.MaxExposurePercent,
        DefaultTakeProfitRrMultiplier = risk.DefaultTakeProfitRrMultiplier,
        AutoApplyDefaultSlTp = risk.AutoApplyDefaultSlTp,
        SafeMode = risk.SafeMode,
        AutoShutdown = risk.AutoShutdown,
        EmergencyHalt = risk.EmergencyHalt,
    };

    public static RiskStatusDto ToDto(RiskProfile risk, decimal currentLeverage) => new()
    {
        DailyDrawdownPercent = risk.DailyDrawdownPercent,
        ExposurePercent = risk.ExposurePercent,
        RiskLevel = risk.RiskLevel.ToString(),
        Leverage = currentLeverage,
    };

    public static HermesStatusDto ToDto(HermesState hermes) => new()
    {
        State = hermes.State.ToString(),
        ActiveStrategy = hermes.ActiveStrategy,
        Confidence = hermes.Confidence,
        Mode = hermes.Mode,
    };

    public static StrategyCardDto ToDto(StrategyState strategy) => new()
    {
        Id = strategy.Id,
        Name = strategy.Name,
        Description = strategy.Description,
        RiskProfile = strategy.RiskProfileLabel,
        Status = strategy.Status.ToString(),
        IsEnabled = strategy.IsEnabled,
    };

    public static MarketTickerDto ToDto(MarketTicker ticker) => new()
    {
        Symbol = ticker.Symbol,
        Price = ticker.Price,
        ChangePercent24h = ticker.ChangePercent24h,
        Volume24h = ticker.Volume24h,
        InWatchlist = ticker.InWatchlist,
    };

    public static LogEntryDto ToDto(PlatformLogEntry entry) => new()
    {
        Timestamp = entry.Timestamp.LocalDateTime,
        EventType = entry.EventType,
        Source = entry.Source,
        Message = entry.Message,
    };

    public static TradeJournalEntryDto ToDto(TradeJournalEntry entry) => new()
    {
        Timestamp = entry.Timestamp.LocalDateTime,
        OrderId = entry.OrderId,
        Symbol = entry.Symbol,
        Kind = entry.Kind,
        Side = entry.Side,
        Quantity = entry.Quantity,
        FillPrice = entry.FillPrice,
        Fee = entry.Fee,
        RealizedPnl = entry.RealizedPnl,
        BalanceBefore = entry.BalanceBefore,
        BalanceAfter = entry.BalanceAfter,
        ReduceOnly = entry.ReduceOnly,
    };

    public static HermesTaskDto ToDto(HermesTask task) => new()
    {
        Title = task.Title,
        Status = task.Status,
    };

    public static HermesDecisionDto ToDto(HermesDecision decision) => new()
    {
        Timestamp = decision.Timestamp.LocalDateTime,
        Summary = decision.Summary,
    };
}
