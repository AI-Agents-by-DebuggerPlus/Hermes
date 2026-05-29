namespace Hermes.SpotTerminal.Shared.Bridge;

public sealed class SpotTerminalSnapshotSection
{
    public bool TerminalRunning { get; init; }
    public string ExecutionMode { get; init; } = "Virtual";
    public string FeedStatus { get; init; } = "";
    public IReadOnlyList<SpotBalanceSnapshot> Balances { get; init; } = [];
    public IReadOnlyList<SpotOrderSnapshot> OpenOrders { get; init; } = [];
    public IReadOnlyList<SpotTickerSnapshot> Tickers { get; init; } = [];
}

public sealed class SpotBalanceSnapshot
{
    public string Asset { get; init; } = "";
    public decimal Free { get; init; }
    public decimal Locked { get; init; }
}

public sealed class SpotOrderSnapshot
{
    public string Id { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public string Type { get; init; } = "";
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public string Status { get; init; } = "";
}

public sealed class SpotTickerSnapshot
{
    public string Symbol { get; init; } = "";
    public decimal Price { get; init; }
    public decimal ChangePercent24h { get; init; }
}
