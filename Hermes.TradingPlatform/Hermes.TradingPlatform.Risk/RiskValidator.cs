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

            return (true, null);
        }

        // Daily-loss circuit breaker. Today PnL is tracked relative to the session
        // baseline; if losses exceed MaxDailyLossPercent of (Balance - PnL.Today)
        // we either reject the order outright or auto-engage safe mode.
        if (state.Risk.MaxDailyLossPercent > 0 && state.Pnl.Today < 0)
        {
            var startingEquity = state.Account.Balance - state.Pnl.Today;
            if (startingEquity > 0)
            {
                var dailyDrawdownPct = -state.Pnl.Today / startingEquity * 100m;
                if (dailyDrawdownPct >= state.Risk.MaxDailyLossPercent)
                {
                    return (false,
                        $"Daily loss {dailyDrawdownPct:F2}% reached cap {state.Risk.MaxDailyLossPercent:F2}%"
                        + (state.Risk.AutoShutdown ? " (auto-shutdown will halt platform)." : "."));
                }
            }
        }

        // Per-trade risk vs balance: notional cannot exceed MaxRiskPerTradePercent of equity.
        // For futures with leverage L, real margin = notional / L; we cap on margin.
        if (state.Risk.MaxRiskPerTradePercent > 0 && state.Account.Balance > 0)
        {
            var leverage = state.Account.Leverage > 0 ? state.Account.Leverage : 1m;
            var notional = order.Price * order.Quantity;
            var marginRequired = notional / leverage;
            var capUsd = state.Account.Balance * (state.Risk.MaxRiskPerTradePercent / 100m);
            if (capUsd > 0 && marginRequired > capUsd)
            {
                return (false,
                    $"Per-trade margin {marginRequired:N2} exceeds cap {capUsd:N2} "
                    + $"({state.Risk.MaxRiskPerTradePercent:F2}% of balance).");
            }
        }

        // Exposure: sum of |position notional| + this order notional must not exceed
        // MaxExposurePercent of equity (computed in margin terms via account leverage).
        if (state.Risk.MaxExposurePercent > 0 && state.Account.Balance > 0)
        {
            var leverage = state.Account.Leverage > 0 ? state.Account.Leverage : 1m;
            var existingMargin = state.Positions.Sum(p => Math.Abs(p.Size * p.MarkPrice)) / leverage;
            var orderMargin = order.Price * order.Quantity / leverage;
            var capUsd = state.Account.Balance * (state.Risk.MaxExposurePercent / 100m);
            if (capUsd > 0 && existingMargin + orderMargin > capUsd)
            {
                return (false,
                    $"Total exposure {existingMargin + orderMargin:N2} exceeds cap {capUsd:N2} "
                    + $"({state.Risk.MaxExposurePercent:F2}% of balance).");
            }
        }

        return (true, null);
    }
}
