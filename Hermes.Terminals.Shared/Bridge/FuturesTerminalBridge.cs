namespace Hermes.Terminals.Shared.Bridge;

public static class FuturesBridgePaths
{
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesFutures");

    public static string BridgeRoot => Path.Combine(DataRoot, "bridge");

    public static string CommandsFile => Path.Combine(BridgeRoot, "commands.json");
    public static string HeartbeatFile => Path.Combine(BridgeRoot, "heartbeat.txt");

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BridgeRoot);
    }
}

public sealed class FuturesPlatformCommandFile
{
    public List<FuturesPlatformCommand> Pending { get; set; } = [];
}

public sealed class FuturesPlatformCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string? Symbol { get; set; }
    public string? Side { get; set; }
    public string? OrderType { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? QuantityUsdt { get; set; }
    public decimal? Price { get; set; }
    public bool? ReduceOnly { get; set; }
    public string? OrderId { get; set; }
    public int? Leverage { get; set; }
    public string? RequestedBy { get; set; }
}

public sealed class FuturesPlatformCommandResultFile
{
    public Guid CommandId { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public decimal? RealizedPnlUsdt { get; init; }
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class FuturesTerminalSnapshotSection
{
    public bool TerminalRunning { get; init; }
    public bool HasCredentials { get; init; }
    public string SelectedSymbol { get; init; } = "";
    public string WsStatus { get; init; } = "";
    public string ChartInterval { get; init; } = "1m";
    public decimal LastPrice { get; init; }
    public decimal ChangePercent24h { get; init; }
    public decimal DefaultAgentOrderUsdt { get; init; }
    public decimal MaxOrderMarginPercent { get; init; }
    public decimal MaxOrderNotionalUsdt { get; init; }
    public int SelectedLeverage { get; init; }
    public decimal MaxTotalExposureUsdt { get; init; }
    public decimal CurrentExposureUsdt { get; init; }
    public decimal AvailableUsdt { get; init; }
    public decimal WalletBalanceUsdt { get; init; }
    public decimal DailyRealizedPnlUsdt { get; init; }
    public int MaxOpenPositions { get; init; }
    public int MaxLeverage { get; init; }
    public bool RiskManagementEnabled { get; init; }
    public IReadOnlyList<FuturesBalanceSnapshot> Balances { get; init; } = [];
    public IReadOnlyList<FuturesPositionSnapshot> Positions { get; init; } = [];
    public IReadOnlyList<FuturesOrderSnapshot> OpenOrders { get; init; } = [];
}

public sealed class FuturesBalanceSnapshot
{
    public string Asset { get; init; } = "";
    public decimal Free { get; init; }
    public decimal Locked { get; init; }
}

public sealed class FuturesPositionSnapshot
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Size { get; init; }
    public decimal NotionalUsdt { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public int Leverage { get; init; }
    public string MarginType { get; init; } = "";
}

public sealed class FuturesOrderSnapshot
{
    public string Id { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public string Type { get; init; } = "";
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal NotionalUsdt { get; init; }
    public string Status { get; init; } = "";
    public decimal StopPrice { get; init; }
}
