using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies.BuiltIn;

/// <summary>Trend follow: positive 24h % → small long market (paper MVP).</summary>
public sealed class MomentumStrategy : ITradingStrategy
{
    private readonly StrategyCooldown _cooldown = new(TimeSpan.FromSeconds(45));
    private decimal _quantity = 0.01m;
    private decimal _changeThreshold = 0.6m;

    public string Id => "momentum";
    public string Name => "Momentum";

    public StrategyParameters DefaultParameters => new()
    {
        StrategyId = Id,
        Quantity = 0.01m,
        ChangeThresholdPercent = 0.6m,
        CooldownSeconds = 45,
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
        if (!IsEnabled(state) || tick.Symbol != "BTCUSDT" || !_cooldown.TryAcquire())
        {
            return null;
        }

        if (tick.ChangePercent24h is not { } change || change <= _changeThreshold)
        {
            return null;
        }

        return new StrategySignal
        {
            Symbol = tick.Symbol,
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = _quantity,
            Price = tick.Price,
            Reason = $"24h change {change:F2}% > {_changeThreshold:F2}% — momentum long",
        };
    }

    private static bool IsEnabled(TradingPlatformState state) =>
        state.Strategies.FirstOrDefault(s => s.Id == "momentum") is { IsEnabled: true, Status: StrategyRunStatus.Running };
}
