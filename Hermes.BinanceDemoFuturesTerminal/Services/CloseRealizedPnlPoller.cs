using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

internal static class CloseRealizedPnlPoller
{
    private const int PollIntervalMs = 500;
    private const int MaxWaitMs = 60_000;

    public static async Task<double?> PollOrderPnlAsync(
        BinanceApiService api,
        string symbol,
        long orderId,
        long startTimeMs,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(MaxWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var trades = await api.GetUserTradesAsync(symbol, startTimeMs).ConfigureAwait(false);
            var fills = trades.Where(t => t.OrderId == orderId).ToList();
            if (fills.Count > 0)
            {
                return fills.Sum(ParseRealizedPnl);
            }

            await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
        }

        return null;
    }

    public static async Task<IReadOnlyDictionary<string, double>> PollMultiSymbolPnlAsync(
        BinanceApiService api,
        IReadOnlyList<string> symbols,
        long startTimeMs,
        CancellationToken ct = default)
    {
        var pending = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow.AddMilliseconds(MaxWaitMs);

        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var symbol in pending.ToList())
            {
                var trades = await api.GetUserTradesAsync(symbol, startTimeMs).ConfigureAwait(false);
                var pnl = trades.Sum(ParseRealizedPnl);
                if (trades.Count > 0 && Math.Abs(pnl) > 1e-12)
                {
                    result[symbol] = pnl;
                    pending.Remove(symbol);
                }
            }

            if (pending.Count == 0)
            {
                break;
            }

            await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
        }

        foreach (var symbol in pending)
        {
            var trades = await api.GetUserTradesAsync(symbol, startTimeMs).ConfigureAwait(false);
            if (trades.Count > 0)
            {
                result[symbol] = trades.Sum(ParseRealizedPnl);
            }
        }

        return result;
    }

    private static double ParseRealizedPnl(UserTradeResponse trade) =>
        double.TryParse(trade.RealizedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnl)
            ? pnl
            : 0;
}
