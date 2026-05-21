using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Risk;

public sealed class RiskValidator : IRiskValidator
{
    public (bool Allowed, string? Reason) ValidateNewOrder(TradingPlatformState state, Order order)
    {
        if (state.Risk.EmergencyHalt)
        {
            return (false, "Emergency halt active — no new orders.");
        }

        if (state.Risk.SafeMode && !order.ReduceOnly)
        {
            return (false, "Safe mode: only reduce-only orders allowed.");
        }

        if (state.Account.Leverage > state.Risk.MaxLeverage)
        {
            return (false, $"Leverage {state.Account.Leverage} exceeds max {state.Risk.MaxLeverage}.");
        }

        if (order.Symbol == "BTCUSDT" && order.Quantity > state.Risk.MaxPositionSizeBtc)
        {
            return (false, $"BTC size {order.Quantity} exceeds max {state.Risk.MaxPositionSizeBtc}.");
        }

        if (order.ReduceOnly)
        {
            var positionSide = order.Side == OrderSide.Buy ? PositionSide.Short : PositionSide.Long;
            var pos = state.Positions.FirstOrDefault(p =>
                p.Symbol == order.Symbol && p.Side == positionSide);
            if (pos is null || pos.Size <= 0)
            {
                return (false, $"Reduce-only {order.Side}: no {positionSide} position on {order.Symbol}.");
            }

            if (order.Quantity > pos.Size)
            {
                return (false, $"Reduce qty {order.Quantity} exceeds position size {pos.Size}.");
            }
        }

        return (true, null);
    }
}
