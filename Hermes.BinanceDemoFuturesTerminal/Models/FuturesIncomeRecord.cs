using System.Text.Json.Serialization;

namespace Hermes.BinanceDemoFuturesTerminal.Models;

public sealed class FuturesIncomeRecord
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("incomeType")]
    public string IncomeType { get; set; } = string.Empty;

    [JsonPropertyName("income")]
    public string Income { get; set; } = "0";

    [JsonPropertyName("asset")]
    public string Asset { get; set; } = "USDT";

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("tranId")]
    public long TranId { get; set; }

    [JsonPropertyName("tradeId")]
    public string TradeId { get; set; } = string.Empty;
}
