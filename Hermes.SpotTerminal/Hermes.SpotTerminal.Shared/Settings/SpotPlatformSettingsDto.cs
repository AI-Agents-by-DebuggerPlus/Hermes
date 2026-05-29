namespace Hermes.SpotTerminal.Shared.Settings;

public sealed class SpotPlatformSettingsDto
{
    public string ExecutionMode { get; set; } = "Virtual";
    public string AgentEventsProvider { get; set; } = "Json";
    public string BinanceApiKey { get; set; } = "";
    public string BinanceApiSecret { get; set; } = "";
    public IReadOnlyList<string> WatchSymbols { get; set; } = ["BTCUSDT", "ETHUSDT", "SOLUSDT"];
}
