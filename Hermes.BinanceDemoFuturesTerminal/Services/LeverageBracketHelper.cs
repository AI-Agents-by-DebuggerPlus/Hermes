using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class LeverageBracketHelper
{
    public static readonly int[] StandardTickMarks = [1, 25, 50, 75, 100, 125];

    public static int GetSymbolMaxLeverage(IReadOnlyList<LeverageBracket> brackets) =>
        brackets.Count > 0 ? brackets.Max(b => b.InitialLeverage) : 125;

    public static decimal GetMaxNotionalUsdt(IReadOnlyList<LeverageBracket> brackets, int leverage)
    {
        if (brackets.Count == 0 || leverage < 1)
        {
            return 0;
        }

        var match = brackets
            .Where(b => b.InitialLeverage >= leverage)
            .OrderBy(b => b.InitialLeverage)
            .FirstOrDefault();

        return match?.NotionalCap ?? 0;
    }

    public static IEnumerable<int> GetVisibleTickMarks(int maxSelectable) =>
        StandardTickMarks.Where(t => t <= maxSelectable);
}
