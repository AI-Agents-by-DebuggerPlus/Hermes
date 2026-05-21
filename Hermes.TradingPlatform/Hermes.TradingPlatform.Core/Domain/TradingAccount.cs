namespace Hermes.TradingPlatform.Core.Domain;

public sealed class TradingAccount
{
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal FreeMargin { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal Leverage { get; set; } = 1m;
}
