namespace Hermes.Wpf.Services;

/// <summary>Relaxed capture gates for successful built-in local automations.</summary>
public sealed class LocalCaptureOptions
{
    public bool BypassMinLength { get; init; } = true;

    public int MinImportanceOverride { get; init; } = 4;

    public bool ForceRoleCaptureWhenDisabled { get; init; }
}
