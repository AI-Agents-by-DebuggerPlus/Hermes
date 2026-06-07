namespace Hermes.Wpf.Models;

public sealed class WhatsAppParseProbeResult
{
    public bool Success { get; init; }

    public int DetectLatencyMs { get; init; }

    public string ProbeText { get; init; } = string.Empty;

    public string FailureReason { get; init; } = string.Empty;
}
