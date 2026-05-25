using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Fade 24h move: sharp drop → long, sharp rally → short (paper MVP).</summary>
public sealed class MeanReversionStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(60));
    private decimal _quantity = 0.05m;
    private decimal _changeThreshold = 0.8m;

    public string Id => "mean-rev";
    public string Name => "Mean Reversion";

    public StrategyParameters DefaultParameters => new()
    {
        StrategyId = Id,
        Quantity = 0.05m,
        ChangeThresholdPercent = 0.8m,
        CooldownSeconds = 60,
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
        if (!IsEnabled(state) || tick.Symbol != "ETHUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is not { } change)
        {
            return null;
        }

        if (change < -_changeThreshold)
        {
            return new StrategySignal
            {
                Symbol = tick.Symbol,
                Side = OrderSide.Buy,
                OrderType = OrderType.Market,
                Quantity = _quantity,
                Price = tick.Price,
                Reason = $"24h {change:F2}% — fade down (long)",
            };
        }

        if (change > _changeThreshold)
        {
            return new StrategySignal
            {
                Symbol = tick.Symbol,
                Side = OrderSide.Sell,
                OrderType = OrderType.Market,
                Quantity = _quantity,
                Price = tick.Price,
                Reason = $"24h +{change:F2}% — fade up (short)",
            };
        }

        return null;
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "mean-rev") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
