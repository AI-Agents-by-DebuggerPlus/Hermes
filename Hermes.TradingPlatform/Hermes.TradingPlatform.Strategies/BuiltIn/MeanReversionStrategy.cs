using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Fade 24h move: sharp drop → long, sharp rally → short (paper MVP).</summary>
public sealed class MeanReversionStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(60));

    public string Id => "mean-rev";
    public string Name => "Mean Reversion";

    public StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state)
    {
        if (!IsEnabled(state) || tick.Symbol != "ETHUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is < -0.8m)
        {
            return new StrategySignal
            {
                Symbol = tick.Symbol,
                Side = OrderSide.Buy,
                OrderType = OrderType.Market,
                Quantity = 0.05m,
                Price = tick.Price,
                Reason = $"24h {tick.ChangePercent24h:F2}% — fade down (long)",
            };
        }

        if (tick.ChangePercent24h is > 0.8m)
        {
            return new StrategySignal
            {
                Symbol = tick.Symbol,
                Side = OrderSide.Sell,
                OrderType = OrderType.Market,
                Quantity = 0.05m,
                Price = tick.Price,
                Reason = $"24h +{tick.ChangePercent24h:F2}% — fade up (short)",
            };
        }

        return null;
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "mean-rev") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
