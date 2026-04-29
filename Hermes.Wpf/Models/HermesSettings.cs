namespace Hermes.Wpf.Models;

public sealed class HermesSettings
{
    public string WslDistro { get; set; } = "Ubuntu";
    public string VenvPath { get; set; } = "~/hermes-agent/venv";
    public string HermesCommand { get; set; } = "hermes";
    public int ChatTimeoutSeconds { get; set; } = 60;
    public bool AutoReconnect { get; set; } = true;
    public bool IsFirstRun { get; set; } = true;
}
