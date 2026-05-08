namespace Hermes.Wpf.Models;

/// <summary>Optional override: <c>%AppData%\HermesWpf\externalBrain.json</c> with <c>{"MemoryPath":"..."}</c>.</summary>
public sealed class ExternalBrainFileConfig
{
    public string MemoryPath { get; set; } = string.Empty;
}
