using System.Text.Json.Serialization;

namespace Hermes.StrategyViewer.Models;

public sealed class StrategyDocument
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Strategy";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "XAUUSD";

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }

    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    [JsonPropertyName("disclaimer")]
    public string? Disclaimer { get; set; }

    [JsonPropertyName("price_snapshot")]
    public PriceSnapshot? PriceSnapshot { get; set; }

    [JsonPropertyName("scenarios")]
    public List<StrategyScenario> Scenarios { get; set; } = [];

    [JsonPropertyName("risk")]
    public RiskBlock? Risk { get; set; }

    [JsonPropertyName("technical")]
    public TechnicalBlock? Technical { get; set; }

    [JsonPropertyName("alerts")]
    public List<StrategyAlert> Alerts { get; set; } = [];
}

public sealed class PriceSnapshot
{
    [JsonPropertyName("last")]
    public double? Last { get; set; }

    [JsonPropertyName("change_abs")]
    public double? ChangeAbs { get; set; }

    [JsonPropertyName("change_pct")]
    public double? ChangePct { get; set; }
}

public sealed class StrategyScenario
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("side")]
    public string? Side { get; set; }

    [JsonPropertyName("accent")]
    public string? Accent { get; set; }

    [JsonPropertyName("preferred")]
    public bool Preferred { get; set; }

    [JsonPropertyName("entry_low")]
    public double? EntryLow { get; set; }

    [JsonPropertyName("entry_high")]
    public double? EntryHigh { get; set; }

    [JsonPropertyName("entry_trigger")]
    public double? EntryTrigger { get; set; }

    [JsonPropertyName("stop_loss")]
    public double? StopLoss { get; set; }

    [JsonPropertyName("take_profit")]
    public List<double>? TakeProfit { get; set; }

    [JsonPropertyName("conditions")]
    public List<StrategyCondition> Conditions { get; set; } = [];
}

public sealed class StrategyCondition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("op")]
    public string? Op { get; set; }

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("value_high")]
    public double? ValueHigh { get; set; }

    [JsonPropertyName("indicator")]
    public string? Indicator { get; set; }

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }
}

public sealed class RiskBlock
{
    [JsonPropertyName("risk_per_trade_pct")]
    public double? RiskPerTradePct { get; set; }

    [JsonPropertyName("leverage")]
    public string? Leverage { get; set; }

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }

    [JsonPropertyName("volatility_label")]
    public string? VolatilityLabel { get; set; }
}

public sealed class TechnicalBlock
{
    [JsonPropertyName("trend")]
    public string? Trend { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("indicators")]
    public List<TechnicalIndicatorSpec> Indicators { get; set; } = [];
}

public sealed class TechnicalIndicatorSpec
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("warn_above")]
    public double? WarnAbove { get; set; }

    [JsonPropertyName("warn_below")]
    public double? WarnBelow { get; set; }
}

public sealed class StrategyAlert
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public enum ConditionStatus
{
    Pending,
    Confirmed,
    Rejected,
}
