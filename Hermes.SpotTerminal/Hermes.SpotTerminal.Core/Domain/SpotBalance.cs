namespace Hermes.SpotTerminal.Core.Domain;

public sealed class SpotBalance
{
    public string Asset { get; set; } = "";
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
}
