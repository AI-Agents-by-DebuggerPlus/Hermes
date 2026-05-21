using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Placeholder sweep logic: limit near market on SOL when |24h| elevated (paper MVP).</summary>
public sealed class LiquiditySweepStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(90));

    public string Id => "liq-sweep";
    public string Name => "Liquidity Sweep";

    public StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state)
    {
        if (!IsEnabled(state) || tick.Symbol != "SOLUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is not (>= 2m or <= -2m))
        {
            return null;
        }

        var side = tick.ChangePercent24h > 0 ? OrderSide.Sell : OrderSide.Buy;
        var limitPrice = side == OrderSide.Buy
            ? tick.Price * 0.998m
            : tick.Price * 1.002m;

        return new StrategySignal
        {
            Symbol = tick.Symbol,
            Side = side,
            OrderType = OrderType.Limit,
            Quantity = 5m,
            Price = limitPrice,
            Reason = $"|24h| {tick.ChangePercent24h:F2}% — sweep limit {side}",
            AutoExecute = true,
        };
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "liq-sweep") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
