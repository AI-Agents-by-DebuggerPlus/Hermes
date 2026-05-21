using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Trend follow: positive 24h % → small long market (paper MVP).</summary>
public sealed class MomentumStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(45));

    public string Id => "momentum";
    public string Name => "Momentum";

    public StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state)
    {
        if (!IsEnabled(state) || tick.Symbol != "BTCUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is not > 0.6m)
        {
            return null;
        }

        return new StrategySignal
        {
            Symbol = tick.Symbol,
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.01m,
            Price = tick.Price,
            Reason = $"24h change {tick.ChangePercent24h:F2}% > 0.6% — momentum long",
        };
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "momentum") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
