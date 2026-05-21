using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.State;

public static class TradingStateCalculator
{
    public static void RecalculateEquity(TradingPlatformState state)
    {
        var unrealized = state.Positions.Sum(p => p.UnrealizedPnl);
        state.Account.Equity = state.Account.Balance + unrealized;
    }
}
