using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data.Seed;

public static class InitialTradingSeed
{
    public static TradingPlatformState Create()
    {
        var state = new TradingPlatformState
        {
            Account = new TradingAccount
            {
                Balance = 100_000m,
                Equity = 102_450.75m,
                FreeMargin = 78_120.50m,
                UsedMargin = 24_330.25m,
                Leverage = 3m,
            },
            Pnl = new PnlTracker
            {
                Today = 1_245.30m,
                Week = 3_890.15m,
                Month = 8_220.00m,
                AllTime = 24_450.75m,
            },
            Risk = new RiskProfile
            {
                MaxDailyLossPercent = 5m,
                MaxPositionSizeBtc = 0.5m,
                MaxLeverage = 5m,
                MaxExposurePercent = 50m,
                SafeMode = true,
                AutoShutdown = true,
                DailyDrawdownPercent = 1.2m,
                ExposurePercent = 34.5m,
                RiskLevel = RiskLevel.Low,
            },
            Hermes = new HermesState
            {
                State = HermesOrchestrationState.Monitoring,
                ActiveStrategy = "Liquidity Sweep",
                Confidence = 0.72m,
                Mode = "Orchestration / Paper",
                CurrentReasoning =
                    "Initial session: monitoring paper portfolio. Hermes does not execute orders.",
                StrategyContext = "Active: Liquidity Sweep. Watchlist: BTC, ETH, SOL.",
            },
        };

        state.Hermes.Tasks.AddRange(
        [
            new HermesTask { Title = "Review open BTC exposure", Status = "In progress" },
            new HermesTask { Title = "Explain liquidity sweep signal", Status = "Queued" },
        ]);

        state.Hermes.Decisions.AddRange(
        [
            new HermesDecision
            {
                Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
                Summary = "Hold ETH short — risk within profile; no scale-in.",
            },
            new HermesDecision
            {
                Timestamp = DateTimeOffset.UtcNow.AddHours(-3),
                Summary = "Declined momentum entry — daily DD headroom < 20%.",
            },
        ]);

        state.Positions.AddRange(
        [
            new Position { Symbol = "BTCUSDT", Side = PositionSide.Long, Size = 0.15m, EntryPrice = 94_200m, MarkPrice = 95_100m, UnrealizedPnl = 135m, RealizedPnl = 0m, LiquidationPrice = 88_500m },
            new Position { Symbol = "ETHUSDT", Side = PositionSide.Short, Size = 2.5m, EntryPrice = 3_450m, MarkPrice = 3_410m, UnrealizedPnl = 100m, RealizedPnl = 45m, LiquidationPrice = 3_720m },
            new Position { Symbol = "SOLUSDT", Side = PositionSide.Long, Size = 120m, EntryPrice = 142.5m, MarkPrice = 141.2m, UnrealizedPnl = -156m, RealizedPnl = 0m, LiquidationPrice = 128m },
        ]);

        state.Orders.AddRange(
        [
            new Order { Id = "o-1001", Symbol = "BTCUSDT", Type = OrderType.Limit, Side = OrderSide.Buy, Price = 93_500m, Quantity = 0.05m, Status = OrderStatus.Open, ReduceOnly = false },
            new Order { Id = "o-1002", Symbol = "ETHUSDT", Type = OrderType.Stop, Side = OrderSide.Sell, Price = 3_380m, TriggerPrice = 3_380m, Quantity = 1m, Status = OrderStatus.Open, ReduceOnly = true },
            new Order { Id = "o-0998", Symbol = "BTCUSDT", Type = OrderType.Market, Side = OrderSide.Buy, Price = 0m, Quantity = 0.1m, Status = OrderStatus.Filled, ReduceOnly = false },
            new Order { Id = "o-0999", Symbol = "SOLUSDT", Type = OrderType.Limit, Side = OrderSide.Sell, Price = 145m, Quantity = 50m, Status = OrderStatus.Cancelled, ReduceOnly = false },
        ]);

        state.Tickers.AddRange(
        [
            new MarketTicker { Symbol = "BTCUSDT", Price = 95_124.50m, ChangePercent24h = 2.34m, Volume24h = 28_400_000_000m, InWatchlist = true },
            new MarketTicker { Symbol = "ETHUSDT", Price = 3_412.80m, ChangePercent24h = -0.85m, Volume24h = 12_100_000_000m, InWatchlist = true },
            new MarketTicker { Symbol = "SOLUSDT", Price = 141.22m, ChangePercent24h = 4.12m, Volume24h = 2_800_000_000m, InWatchlist = true },
            new MarketTicker { Symbol = "BNBUSDT", Price = 612.40m, ChangePercent24h = 0.42m, Volume24h = 890_000_000m, InWatchlist = false },
        ]);

        state.Strategies.AddRange(
        [
            new StrategyState { Id = "liq-sweep", Name = "Liquidity Sweep", Description = "Sweep highs/lows with volume confirmation.", RiskProfileLabel = "Moderate", Status = StrategyRunStatus.Running, IsEnabled = true },
            new StrategyState { Id = "momentum", Name = "Momentum", Description = "Trend continuation on 15m breakout.", RiskProfileLabel = "Aggressive", Status = StrategyRunStatus.Idle, IsEnabled = false },
            new StrategyState { Id = "mean-rev", Name = "Mean Reversion", Description = "Fade extremes on 1h VWAP bands.", RiskProfileLabel = "Conservative", Status = StrategyRunStatus.Idle, IsEnabled = true },
        ]);

        state.Logs.AddRange(
        [
            new PlatformLogEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2), EventType = "Order", Source = "VirtualExchange", Message = "Limit order o-1001 accepted (BTCUSDT Buy 0.05)" },
            new PlatformLogEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), EventType = "Risk", Source = "RiskManager", Message = "Daily DD 1.2% — within limits" },
            new PlatformLogEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-8), EventType = "Market", Source = "MockFeed", Message = "BTCUSDT tick 95100 → 95124" },
            new PlatformLogEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-12), EventType = "Strategy", Source = "LiquiditySweep", Message = "Signal: no entry — spread too wide" },
            new PlatformLogEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15), EventType = "System", Source = "Platform", Message = "Paper trading session started (Phase 2+3 backend)" },
        ]);

        return state;
    }
}
