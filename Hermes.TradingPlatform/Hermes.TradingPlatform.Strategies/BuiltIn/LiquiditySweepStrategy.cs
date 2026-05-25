using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Liquidity sweep: limit near market on SOL when |24h| elevated (paper MVP).</summary>
public sealed class LiquiditySweepStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(90));
    private decimal _quantity = 5m;
    private decimal _changeThreshold = 2m;

    public string Id => "liq-sweep";
    public string Name => "Liquidity Sweep";

    public StrategyParameters DefaultParameters => new()
    {
        StrategyId = Id,
        Quantity = 5m,
        ChangeThresholdPercent = 2m,
        CooldownSeconds = 90,
    };

    public void ApplyParameters(StrategyParameters parameters)
    {
        if (parameters.Quantity > 0)
        {
            _quantity = parameters.Quantity;
        }

        if (parameters.ChangeThresholdPercent > 0)
        {
            _changeThreshold = parameters.ChangeThresholdPercent;
        }

        if (parameters.CooldownSeconds > 0)
        {
            _cooldown.Interval = TimeSpan.FromSeconds(parameters.CooldownSeconds);
        }
    }

    public StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state)
    {
        if (!IsEnabled(state) || tick.Symbol != "SOLUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is not { } change || Math.Abs(change) < _changeThreshold)
        {
            return null;
        }

        var side = change > 0 ? OrderSide.Sell : OrderSide.Buy;
        var limitPrice = side == OrderSide.Buy
            ? tick.Price * 0.998m
            : tick.Price * 1.002m;

        return new StrategySignal
        {
            Symbol = tick.Symbol,
            Side = side,
            OrderType = OrderType.Limit,
            Quantity = _quantity,
            Price = limitPrice,
            Reason = $"|24h| {change:F2}% — sweep limit {side}",
            AutoExecute = true,
        };
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "liq-sweep") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
