namespace Hermes.TradingPlatform.Core.Domain;

public sealed class TradingPlatformState
{
    public TradingAccount Account { get; set; } = new();
    public PnlTracker Pnl { get; set; } = new();
    public RiskProfile Risk { get; set; } = new();
    public HermesState Hermes { get; set; } = new();
    public List<Position> Positions { get; } = [];
    public List<Order> Orders { get; } = [];
    public List<MarketTicker> Tickers { get; } = [];
    public List<StrategyState> Strategies { get; } = [];
    public List<PlatformLogEntry> Logs { get; } = [];
    public List<TradeJournalEntry> Journal { get; } = [];
}
