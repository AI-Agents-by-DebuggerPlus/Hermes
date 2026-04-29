namespace Hermes.Wpf.Models;

public sealed class ConnectionStatus
{
    public ConnectionState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime CheckedAt { get; init; } = DateTime.Now;

    /// <summary>Optional step-by-step diagnostics for UI and log files.</summary>
    public IReadOnlyList<CheckResult>? Diagnostics { get; init; }
}
