using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Hermes.BinanceDemoFuturesTerminal.MVVM;

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

    [JsonPropertyName("marginType")]
    public string MarginType { get; set; } = "cross";

    [JsonPropertyName("liquidationPrice")]
    public string LiquidationPrice { get; set; } = "0";

    [JsonPropertyName("breakEvenPrice")]
    public string BreakEvenPrice { get; set; } = "0";

    [JsonPropertyName("isolatedMargin")]
    public string IsolatedMargin { get; set; } = "0";

    [JsonPropertyName("notional")]
    public string Notional { get; set; } = "0";

    [JsonPropertyName("maintMargin")]
    public string MaintMargin { get; set; } = "0";

    [JsonPropertyName("positionSide")]
    public string PositionSide { get; set; } = "BOTH";
}

public sealed class PositionModel : ObservableObject
{
    private string _closeLimitPriceText = string.Empty;
    private string _closeQuantityText = string.Empty;
    private double? _stopLoss;
    private double? _takeProfit;

    public string Symbol { get; set; } = string.Empty;
    public double Size { get; set; }
    public double EntryPrice { get; set; }
    public double MarkPrice { get; set; }
    public double UnrealizedPnl { get; set; }
    public int Leverage { get; set; }
    public string Side { get; set; } = string.Empty;
    public double LiquidationPrice { get; set; }
    public double BreakEvenPrice { get; set; }
    public double InitialMargin { get; set; }
    public double MaintMargin { get; set; }
    public double NotionalUsdt { get; set; }
    public FuturesMarginType MarginType { get; set; } = FuturesMarginType.Cross;
    public string ContractBadge { get; set; } = "Бесср";

    public double? StopLoss
    {
        get => _stopLoss;
        set
        {
            if (SetProperty(ref _stopLoss, value))
            {
                OnPropertyChanged(nameof(StopLossDisplay));
            }
        }
    }

    public double? TakeProfit
    {
        get => _takeProfit;
        set
        {
            if (SetProperty(ref _takeProfit, value))
            {
                OnPropertyChanged(nameof(TakeProfitDisplay));
            }
        }
    }

    public string CloseLimitPriceText
    {
        get => _closeLimitPriceText;
        set => SetProperty(ref _closeLimitPriceText, value);
    }

    public string CloseQuantityText
    {
        get => _closeQuantityText;
        set => SetProperty(ref _closeQuantityText, value);
    }

    public string SizeDisplay => (Math.Abs(Size) * MarkPrice).ToString("N2", CultureInfo.InvariantCulture);
    public string EntryDisplay => EntryPrice.ToString("N2", CultureInfo.InvariantCulture);
    public string BreakEvenDisplay =>
        BreakEvenPrice > 0 ? BreakEvenPrice.ToString("N2", CultureInfo.InvariantCulture) : "—";
    public string MarkDisplay => MarkPrice.ToString("N2", CultureInfo.InvariantCulture);
    public string LiquidationDisplay =>
        LiquidationPrice > 0 ? LiquidationPrice.ToString("N2", CultureInfo.InvariantCulture) : "—";
    public string MarginRatioDisplay =>
        InitialMargin > 0 ? $"{MaintMargin / InitialMargin * 100:F2}%" : "—";
    public string MarginDisplay =>
        $"{InitialMargin.ToString("N2", CultureInfo.InvariantCulture)} USDT ({MarginType.ToMarginLabel()})";
    public string PnlDisplay => (UnrealizedPnl >= 0 ? "+" : string.Empty) + UnrealizedPnl.ToString("N2", CultureInfo.InvariantCulture);
    public double RoiPercent => InitialMargin > 0 ? UnrealizedPnl / InitialMargin * 100 : 0;
    public string RoiDisplay => (RoiPercent >= 0 ? "+" : string.Empty) + RoiPercent.ToString("N2", CultureInfo.InvariantCulture) + "%";
    public string FundingFeeDisplay => "0,00 USDT";
    public string LeverageBadge => $"{Leverage}x";
    public string StopLossDisplay => StopLoss.HasValue ? StopLoss.Value.ToString("N2", CultureInfo.InvariantCulture) : "—";
    public string TakeProfitDisplay => TakeProfit.HasValue ? TakeProfit.Value.ToString("N2", CultureInfo.InvariantCulture) : "—";
    public bool IsLong => Size > 0;
    public bool IsPnlPositive => UnrealizedPnl >= 0;

    public void InitializeCloseFields(string? formattedQty, string? formattedPrice)
    {
        CloseQuantityText = formattedQty ?? Math.Abs(Size).ToString(CultureInfo.InvariantCulture);
        CloseLimitPriceText = formattedPrice ?? MarkPrice.ToString(CultureInfo.InvariantCulture);
    }
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
