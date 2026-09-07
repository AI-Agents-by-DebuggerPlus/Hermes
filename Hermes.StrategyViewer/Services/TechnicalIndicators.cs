namespace Hermes.StrategyViewer.Services;

public static class TechnicalIndicators
{
    public static double? Ema(IReadOnlyList<double> closes, int period)
    {
        if (closes.Count < period || period <= 0)
        {
            return null;
        }

        var k = 2.0 / (period + 1);
        var ema = closes.Take(period).Average();
        for (var i = period; i < closes.Count; i++)
        {
            ema = closes[i] * k + ema * (1 - k);
        }

        return ema;
    }

    public static double? Rsi(IReadOnlyList<double> closes, int period = 14)
    {
        if (closes.Count <= period)
        {
            return null;
        }

        double gain = 0;
        double loss = 0;
        for (var i = 1; i <= period; i++)
        {
            var delta = closes[i] - closes[i - 1];
            if (delta >= 0)
            {
                gain += delta;
            }
            else
            {
                loss -= delta;
            }
        }

        gain /= period;
        loss /= period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var delta = closes[i] - closes[i - 1];
            var g = delta > 0 ? delta : 0;
            var l = delta < 0 ? -delta : 0;
            gain = (gain * (period - 1) + g) / period;
            loss = (loss * (period - 1) + l) / period;
        }

        if (loss == 0)
        {
            return 100;
        }

        var rs = gain / loss;
        return 100 - 100 / (1 + rs);
    }

    public static double? StochasticK(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14)
    {
        if (highs.Count < period || lows.Count < period || closes.Count < period)
        {
            return null;
        }

        var start = closes.Count - period;
        var hh = highs.Skip(start).Take(period).Max();
        var ll = lows.Skip(start).Take(period).Min();
        if (Math.Abs(hh - ll) < double.Epsilon)
        {
            return 50;
        }

        var close = closes[^1];
        return (close - ll) / (hh - ll) * 100;
    }
}
