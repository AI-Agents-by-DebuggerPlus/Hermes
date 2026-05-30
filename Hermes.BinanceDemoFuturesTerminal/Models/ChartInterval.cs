namespace Hermes.BinanceDemoFuturesTerminal.Models;

public sealed class ChartIntervalOption
{
    public string Label { get; init; } = string.Empty;
    public string ApiInterval { get; init; } = string.Empty;
    public string StreamSuffix { get; init; } = string.Empty;
    public int CandleLimit { get; init; } = 150;

    public static IReadOnlyList<ChartIntervalOption> All { get; } =
    [
        new() { Label = "1м", ApiInterval = "1m", StreamSuffix = "kline_1m", CandleLimit = 150 },
        new() { Label = "5м", ApiInterval = "5m", StreamSuffix = "kline_5m", CandleLimit = 150 },
        new() { Label = "15м", ApiInterval = "15m", StreamSuffix = "kline_15m", CandleLimit = 150 },
        new() { Label = "1ч", ApiInterval = "1h", StreamSuffix = "kline_1h", CandleLimit = 150 },
        new() { Label = "4ч", ApiInterval = "4h", StreamSuffix = "kline_4h", CandleLimit = 120 },
        new() { Label = "1д", ApiInterval = "1d", StreamSuffix = "kline_1d", CandleLimit = 90 },
    ];

    public static ChartIntervalOption Parse(string? apiInterval)
    {
        if (!string.IsNullOrWhiteSpace(apiInterval))
        {
            var match = All.FirstOrDefault(x =>
                x.ApiInterval.Equals(apiInterval, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return All[0];
    }
}
