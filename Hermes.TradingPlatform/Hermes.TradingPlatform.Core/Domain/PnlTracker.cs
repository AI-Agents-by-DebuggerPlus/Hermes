namespace Hermes.TradingPlatform.Core.Domain;

public sealed class PnlTracker
{
    public decimal Today { get; set; }
    public decimal Week { get; set; }
    public decimal Month { get; set; }
    public decimal AllTime { get; set; }
}
