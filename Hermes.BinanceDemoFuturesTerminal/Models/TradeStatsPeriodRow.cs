namespace Hermes.BinanceDemoFuturesTerminal.Models;

using Hermes.BinanceDemoFuturesTerminal.Services;

public sealed class TradeStatsPeriodRow
{
    public required string PeriodLabel { get; init; }
    public double RealizedPnl { get; init; }
    public double Commission { get; init; }

    public string PnlDisplay => TradeStatsFormatter.FormatSignedPnl(RealizedPnl);
    public string CommissionDisplay => TradeStatsFormatter.FormatCommission(Commission);
    public bool IsPnlPositive => RealizedPnl > 1e-12;
    public bool IsPnlNegative => RealizedPnl < -1e-12;
}
