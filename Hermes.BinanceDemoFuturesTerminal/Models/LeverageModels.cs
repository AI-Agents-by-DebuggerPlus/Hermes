using System.Text.Json.Serialization;

namespace Hermes.BinanceDemoFuturesTerminal.Models;

public sealed class LeverageBracketResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("brackets")]
    public List<LeverageBracket> Brackets { get; set; } = [];
}

public sealed class LeverageBracket
{
    [JsonPropertyName("bracket")]
    public int Bracket { get; set; }

    [JsonPropertyName("initialLeverage")]
    public int InitialLeverage { get; set; }

    [JsonPropertyName("notionalCap")]
    public decimal NotionalCap { get; set; }

    [JsonPropertyName("notionalFloor")]
    public decimal NotionalFloor { get; set; }
}
