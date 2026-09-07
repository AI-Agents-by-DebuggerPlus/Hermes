using Hermes.StrategyViewer.Models;

namespace Hermes.StrategyViewer.Services;

public sealed class ConditionEvaluation
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public ConditionStatus Status { get; init; }
    public string Detail { get; init; } = "";
}

public static class ScenarioEvaluator
{
    public static IReadOnlyList<ConditionEvaluation> Evaluate(
        StrategyScenario scenario,
        MarketSnapshot market)
    {
        var list = new List<ConditionEvaluation>();
        foreach (var c in scenario.Conditions)
        {
            list.Add(EvaluateOne(c, market));
        }

        return list;
    }

    public static ConditionStatus AggregateStatus(IReadOnlyList<ConditionEvaluation> items)
    {
        if (items.Count == 0)
        {
            return ConditionStatus.Pending;
        }

        if (items.Any(i => i.Status == ConditionStatus.Rejected))
        {
            return ConditionStatus.Rejected;
        }

        if (items.All(i => i.Status == ConditionStatus.Confirmed))
        {
            return ConditionStatus.Confirmed;
        }

        return ConditionStatus.Pending;
    }

    private static ConditionEvaluation EvaluateOne(StrategyCondition c, MarketSnapshot m)
    {
        if (!string.IsNullOrWhiteSpace(m.FetchError))
        {
            return new ConditionEvaluation
            {
                Id = c.Id,
                Label = c.Label,
                Status = ConditionStatus.Pending,
                Detail = "нет live-данных",
            };
        }

        var price = m.LastPrice;
        return c.Kind switch
        {
            "price_in_range" => Range(price, c),
            "price_above" => Compare(price, c.Value, ">", c),
            "price_below" => Compare(price, c.Value, "<", c),
            "indicator_cmp" => IndicatorCompare(c, m),
            "candle_close_above" => CandleAbove(c, m),
            "candle_close_below" => CandleBelow(c, m),
            "manual" => Manual(c, m),
            _ => new ConditionEvaluation
            {
                Id = c.Id,
                Label = c.Label,
                Status = ConditionStatus.Pending,
                Detail = c.Kind,
            },
        };
    }

    private static ConditionEvaluation Range(double price, StrategyCondition c)
    {
        var lo = c.Value ?? 0;
        var hi = c.ValueHigh ?? lo;
        var ok = price >= lo && price <= hi;
        return new ConditionEvaluation
        {
            Id = c.Id,
            Label = c.Label,
            Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
            Detail = $"price={price:F2} zone {lo:F0}–{hi:F0}",
        };
    }

    private static ConditionEvaluation Compare(double price, double? level, string op, StrategyCondition c)
    {
        if (level is null)
        {
            return Pending(c, "no level");
        }

        var ok = op == ">" ? price > level : price < level;
        return new ConditionEvaluation
        {
            Id = c.Id,
            Label = c.Label,
            Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
            Detail = $"price={price:F2} {op} {level:F2}",
        };
    }

    private static ConditionEvaluation IndicatorCompare(StrategyCondition c, MarketSnapshot m)
    {
        var val = ResolveIndicator(c.Indicator, m);
        if (val is null || c.Value is null)
        {
            return Pending(c, "indicator n/a");
        }

        var ok = c.Op switch
        {
            "<=" => val <= c.Value,
            "<" => val < c.Value,
            ">=" => val >= c.Value,
            ">" => val > c.Value,
            _ => false,
        };

        return new ConditionEvaluation
        {
            Id = c.Id,
            Label = c.Label,
            Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
            Detail = $"{c.Indicator}={val:F2} {c.Op} {c.Value:F2}",
        };
    }

    private static ConditionEvaluation CandleAbove(StrategyCondition c, MarketSnapshot m)
    {
        if (m.LastClosedH1 is null || c.Value is null)
        {
            return Pending(c, "H1 close n/a");
        }

        var ok = m.LastClosedH1 > c.Value;
        return new ConditionEvaluation
        {
            Id = c.Id,
            Label = c.Label,
            Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
            Detail = $"H1 close={m.LastClosedH1:F2} > {c.Value:F2}",
        };
    }

    private static ConditionEvaluation CandleBelow(StrategyCondition c, MarketSnapshot m)
    {
        if (m.LastClosedH1 is null || c.Value is null)
        {
            return Pending(c, "H1 close n/a");
        }

        var ok = m.LastClosedH1 < c.Value;
        return new ConditionEvaluation
        {
            Id = c.Id,
            Label = c.Label,
            Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
            Detail = $"H1 close={m.LastClosedH1:F2} < {c.Value:F2}",
        };
    }

    private static ConditionEvaluation Manual(StrategyCondition c, MarketSnapshot m)
    {
        if (c.Indicator == "ema_10" && m.Ema10Daily is not null)
        {
            var ok = m.LastPrice > m.Ema10Daily;
            return new ConditionEvaluation
            {
                Id = c.Id,
                Label = c.Label,
                Status = ok ? ConditionStatus.Confirmed : ConditionStatus.Pending,
                Detail = $"price={m.LastPrice:F2} vs EMA10={m.Ema10Daily:F2}",
            };
        }

        return Pending(c, "manual");
    }

    private static double? ResolveIndicator(string? id, MarketSnapshot m) =>
        id switch
        {
            "rsi_14" => m.Rsi14Daily,
            "stoch_k" => m.StochKDaily,
            "ema_10" => m.Ema10Daily,
            _ => null,
        };

    private static ConditionEvaluation Pending(StrategyCondition c, string detail) =>
        new()
        {
            Id = c.Id,
            Label = c.Label,
            Status = ConditionStatus.Pending,
            Detail = detail,
        };
}
