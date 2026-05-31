using Hermes.Terminals.Shared.Bridge;

namespace Hermes.Wpf.Services;

internal static class RiskBasedQuantityCalculator
{
    public static decimal ResolveDefaultUsdt(FuturesTerminalSnapshotSection? futures)
    {
        if (futures is null)
        {
            return 50m;
        }

        var preferred = futures.DefaultAgentOrderUsdt > 0 ? futures.DefaultAgentOrderUsdt : 50m;
        if (!futures.RiskManagementEnabled)
        {
            return preferred;
        }

        var capped = preferred;
        if (futures.MaxOrderNotionalUsdt > 0)
        {
            capped = Math.Min(capped, futures.MaxOrderNotionalUsdt);
        }
        else if (futures.AvailableUsdt > 0 && futures.MaxOrderMarginPercent > 0 && futures.SelectedLeverage > 0)
        {
            var maxMargin = futures.AvailableUsdt * futures.MaxOrderMarginPercent / 100m;
            var maxNotional = maxMargin * futures.SelectedLeverage;
            capped = Math.Min(capped, maxNotional);
        }

        if (futures.AvailableUsdt > 0)
        {
            capped = Math.Min(capped, futures.AvailableUsdt);
        }

        var headroom = futures.MaxTotalExposureUsdt - futures.CurrentExposureUsdt;
        if (futures.MaxTotalExposureUsdt > 0 && headroom > 0)
        {
            capped = Math.Min(capped, headroom);
        }
        else if (futures.MaxTotalExposureUsdt > 0 && headroom <= 0)
        {
            capped = Math.Min(capped, 1m);
        }

        return capped > 0 ? capped : 1m;
    }
}
