using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hermes.BinanceDemoFuturesTerminal.Models;

public sealed class AccountInfoResponse
{
    [JsonPropertyName("canTrade")]
    public bool CanTrade { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }

    [JsonPropertyName("assets")]
    public List<FuturesAsset> Assets { get; set; } = [];
}

public sealed class FuturesAsset
{
    [JsonPropertyName("asset")]
    public string Asset { get; set; } = string.Empty;

    [JsonPropertyName("walletBalance")]
    public string WalletBalance { get; set; } = "0";

    [JsonPropertyName("availableBalance")]
    public string AvailableBalance { get; set; } = "0";
}

public sealed class BalanceModel
{
    public string Asset { get; set; } = string.Empty;
    public double Free { get; set; }
    public double Locked { get; set; }
    public double Total => Free + Locked;

    public string FreeDisplay => Free.ToString("N4");
    public string LockedDisplay => Locked.ToString("N4");
    public string TotalDisplay => Total.ToString("N4");
}

public sealed class PositionRiskResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("positionAmt")]
    public string PositionAmt { get; set; } = "0";

    [JsonPropertyName("entryPrice")]
    public string EntryPrice { get; set; } = "0";

    [JsonPropertyName("markPrice")]
    public string MarkPrice { get; set; } = "0";

    [JsonPropertyName("unRealizedProfit")]
    public string UnRealizedProfit { get; set; } = "0";

    [JsonPropertyName("leverage")]
    public string Leverage { get; set; } = "1";

    [JsonPropertyName("positionSide")]
    public string PositionSide { get; set; } = "BOTH";
}

public sealed class PositionModel
{
    public string Symbol { get; set; } = string.Empty;
    public double Size { get; set; }
    public double EntryPrice { get; set; }
    public double MarkPrice { get; set; }
    public double UnrealizedPnl { get; set; }
    public int Leverage { get; set; }
    public string Side { get; set; } = string.Empty;
    public double? StopLoss { get; set; }
    public double? TakeProfit { get; set; }

    public string SizeDisplay => Math.Abs(Size).ToString("N4");
    public string EntryDisplay => EntryPrice.ToString("N2");
    public string MarkDisplay => MarkPrice.ToString("N2");
    public string PnlDisplay => (UnrealizedPnl >= 0 ? "+" : "") + UnrealizedPnl.ToString("N2");
    public string StopLossDisplay => StopLoss.HasValue ? StopLoss.Value.ToString("N2") : "—";
    public string TakeProfitDisplay => TakeProfit.HasValue ? TakeProfit.Value.ToString("N2") : "—";
    public bool IsLong => Size > 0;
}

public sealed class BinanceOrder
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public long OrderId { get; set; }

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("origQty")]
    public string OrigQty { get; set; } = "0";

    [JsonPropertyName("executedQty")]
    public string ExecutedQty { get; set; } = "0";

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("stopPrice")]
    public string StopPrice { get; set; } = "0";

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }
}

public sealed class OrderModel
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string Side { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Price { get; set; }
    public double OrigQty { get; set; }
    public double ExecutedQty { get; set; }
    public string Status { get; set; } = string.Empty;

    public string TimeDisplay => Time.ToString("yyyy-MM-dd HH:mm:ss");
    public string PriceDisplay => Price > 0 ? Price.ToString("N4") : "MARKET";
    public string AmountDisplay => OrigQty.ToString("N4");
    public string ExecutedDisplay => ExecutedQty.ToString("N4");
    public string SideDisplay => IsBuy ? "LONG" : "SHORT";
    public bool IsBuy => Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
}
