using Hermes.TradingPlatform.Shared.Mock;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>Phase 1 mock data provider. Replaced by live state in Phase 2+.</summary>
public sealed class MockTradingDataService
{
    public AccountSummaryDto GetAccountSummary() => new()
    {
        Balance = 100_000m,
        Equity = 102_450.75m,
        FreeMargin = 78_120.50m,
        UsedMargin = 24_330.25m,
    };

    public PnlSummaryDto GetPnlSummary() => new()
    {
        Today = 1_245.30m,
        Week = 3_890.15m,
        Month = 8_220.00m,
        AllTime = 24_450.75m,
    };

    public IReadOnlyList<PositionDto> GetOpenPositions() =>
    [
        new() { Symbol = "BTCUSDT", Side = "Long", Size = 0.15m, EntryPrice = 94_200m, MarkPrice = 95_100m, UnrealizedPnl = 135m, RealizedPnl = 0m, LiquidationPrice = 88_500m },
        new() { Symbol = "ETHUSDT", Side = "Short", Size = 2.5m, EntryPrice = 3_450m, MarkPrice = 3_410m, UnrealizedPnl = 100m, RealizedPnl = 45m, LiquidationPrice = 3_720m },
        new() { Symbol = "SOLUSDT", Side = "Long", Size = 120m, EntryPrice = 142.5m, MarkPrice = 141.2m, UnrealizedPnl = -156m, RealizedPnl = 0m, LiquidationPrice = 128m },
    ];

    public IReadOnlyList<OrderDto> GetActiveOrders() =>
    [
        new() { Id = "o-1001", Symbol = "BTCUSDT", Type = "Limit", Side = "Buy", Price = 93_500m, Quantity = 0.05m, Status = "Open", ReduceOnly = false },
        new() { Id = "o-1002", Symbol = "ETHUSDT", Type = "Stop", Side = "Sell", Price = 3_380m, Quantity = 1m, Status = "Open", ReduceOnly = true },
    ];

    public IReadOnlyList<OrderDto> GetAllOrders() =>
    [
        ..GetActiveOrders(),
        new() { Id = "o-0998", Symbol = "BTCUSDT", Type = "Market", Side = "Buy", Price = 0m, Quantity = 0.1m, Status = "Filled", ReduceOnly = false },
        new() { Id = "o-0999", Symbol = "SOLUSDT", Type = "Limit", Side = "Sell", Price = 145m, Quantity = 50m, Status = "Cancelled", ReduceOnly = false },
    ];

    public RiskStatusDto GetRiskStatus() => new()
    {
        DailyDrawdownPercent = 1.2m,
        ExposurePercent = 34.5m,
        RiskLevel = "Low",
        Leverage = 3m,
    };

    public HermesStatusDto GetHermesStatus() => new()
    {
        State = "Monitoring",
        ActiveStrategy = "Liquidity Sweep",
        Confidence = 0.72m,
        Mode = "Paper / Simulation",
    };

    public IReadOnlyList<StrategyCardDto> GetStrategies() =>
    [
        new() { Id = "liq-sweep", Name = "Liquidity Sweep", Description = "Sweep highs/lows with volume confirmation.", RiskProfile = "Moderate", Status = "Running", IsEnabled = true },
        new() { Id = "momentum", Name = "Momentum", Description = "Trend continuation on 15m breakout.", RiskProfile = "Aggressive", Status = "Idle", IsEnabled = false },
        new() { Id = "mean-rev", Name = "Mean Reversion", Description = "Fade extremes on 1h VWAP bands.", RiskProfile = "Conservative", Status = "Idle", IsEnabled = true },
    ];

    public IReadOnlyList<MarketTickerDto> GetMarketWatch() =>
    [
        new() { Symbol = "BTCUSDT", Price = 95_124.50m, ChangePercent24h = 2.34m, Volume24h = 28_400_000_000m, InWatchlist = true },
        new() { Symbol = "ETHUSDT", Price = 3_412.80m, ChangePercent24h = -0.85m, Volume24h = 12_100_000_000m, InWatchlist = true },
        new() { Symbol = "SOLUSDT", Price = 141.22m, ChangePercent24h = 4.12m, Volume24h = 2_800_000_000m, InWatchlist = true },
        new() { Symbol = "BNBUSDT", Price = 612.40m, ChangePercent24h = 0.42m, Volume24h = 890_000_000m, InWatchlist = false },
    ];

    public IReadOnlyList<LogEntryDto> GetLogs() =>
    [
        new() { Timestamp = DateTime.Now.AddMinutes(-2), EventType = "Order", Source = "VirtualExchange", Message = "Limit order o-1001 accepted (BTCUSDT Buy 0.05)" },
        new() { Timestamp = DateTime.Now.AddMinutes(-5), EventType = "Risk", Source = "RiskManager", Message = "Daily DD 1.2% — within limits" },
        new() { Timestamp = DateTime.Now.AddMinutes(-8), EventType = "Market", Source = "MockFeed", Message = "BTCUSDT tick 95100 → 95124" },
        new() { Timestamp = DateTime.Now.AddMinutes(-12), EventType = "Strategy", Source = "LiquiditySweep", Message = "Signal: no entry — spread too wide" },
        new() { Timestamp = DateTime.Now.AddMinutes(-15), EventType = "System", Source = "Platform", Message = "Paper trading session started" },
    ];

    public IReadOnlyList<HermesTaskDto> GetHermesTasks() =>
    [
        new() { Title = "Review open BTC exposure", Status = "In progress" },
        new() { Title = "Explain liquidity sweep signal", Status = "Queued" },
    ];

    public IReadOnlyList<HermesDecisionDto> GetHermesDecisions() =>
    [
        new() { Timestamp = DateTime.Now.AddHours(-1), Summary = "Hold ETH short — risk within profile; no scale-in." },
        new() { Timestamp = DateTime.Now.AddHours(-3), Summary = "Declined momentum entry — daily DD headroom < 20%." },
    ];
}
