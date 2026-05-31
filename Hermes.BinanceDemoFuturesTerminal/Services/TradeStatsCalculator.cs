using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class TradeStatsCalculator
{
    private static readonly (string Label, Func<DateTime, DateTime> Start)[] Periods =
    [
        ("День", now => now.Date),
        ("Неделя", now => now.Date.AddDays(-6)),
        ("Месяц", now => now.Date.AddDays(-29)),
        ("Все время", _ => DateTime.MinValue),
    ];

    public static IReadOnlyList<TradeStatsPeriodRow> Build(
        IReadOnlyList<UserTradeModel> trades,
        IReadOnlyList<FuturesIncomeRecord> income,
        Action<string>? log = null)
    {
        var tradeRows = FromTrades(trades, log, "[trade-stats] trades");
        var tradeActivity = trades.Sum(t => Math.Abs(t.RealizedPnl) + t.Commission);
        var incomeRows = income
            .Where(IsRelevantIncome)
            .ToList();
        var incomeActivity = incomeRows.Sum(r =>
            Math.Abs(ParseIncome(r.Income)));

        log?.Invoke(
            $"[trade-stats] calc choose source: fills={trades.Count} activity={tradeActivity:F6} | "
            + $"income={incomeRows.Count} activity={incomeActivity:F6}");

        if (trades.Count > 0 && tradeActivity > 1e-10)
        {
            log?.Invoke("[trade-stats] calc using userTrades");
            return tradeRows;
        }

        if (incomeRows.Count > 0 && incomeActivity > 1e-10)
        {
            log?.Invoke("[trade-stats] calc using income API (fallback)");
            return FromIncome(incomeRows, log);
        }

        log?.Invoke("[trade-stats] calc no activity in trades or income — returning zeros");
        return tradeRows;
    }

    public static IReadOnlyList<TradeStatsPeriodRow> FromTrades(
        IEnumerable<UserTradeModel> trades,
        Action<string>? log = null,
        string prefix = "[trade-stats]")
    {
        var list = trades.ToList();
        var now = DateTime.Now;
        log?.Invoke(
            $"{prefix} input fills={list.Count} totalPnl={list.Sum(t => t.RealizedPnl):F6} "
            + $"totalComm={list.Where(t => t.CommissionAsset.Equals("USDT", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Commission):F6}");

        if (list.Count > 0)
        {
            var sample = list.OrderByDescending(t => t.Time).First();
            log?.Invoke(
                $"{prefix} sample fill: {sample.Symbol} {sample.Time:yyyy-MM-dd HH:mm} "
                + $"pnl={sample.RealizedPnl} comm={sample.Commission} {sample.CommissionAsset}");
        }

        return BuildPeriodRows(now, list.Count, periodStart =>
        {
            var filtered = FilterTrades(list, periodStart).ToList();
            return (
                filtered.Count,
                filtered.Sum(t => t.RealizedPnl),
                filtered
                    .Where(t => t.CommissionAsset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => t.Commission));
        }, log, prefix);
    }

    public static IReadOnlyList<TradeStatsPeriodRow> FromIncome(
        IEnumerable<FuturesIncomeRecord> income,
        Action<string>? log = null)
    {
        var list = income.Where(IsRelevantIncome).ToList();
        var now = DateTime.Now;
        log?.Invoke(
            $"[trade-stats] income input rows={list.Count} "
            + $"pnl={list.Where(r => r.IncomeType == "REALIZED_PNL").Sum(r => ParseIncome(r.Income)):F6} "
            + $"comm={list.Where(r => r.IncomeType == "COMMISSION").Sum(r => Math.Abs(ParseIncome(r.Income))):F6}");

        if (list.Count > 0)
        {
            var sample = list.OrderByDescending(r => r.Time).First();
            log?.Invoke(
                $"[trade-stats] income sample: {sample.Symbol} {sample.IncomeType} "
                + $"{ToLocal(sample.Time):yyyy-MM-dd HH:mm} {sample.Income} {sample.Asset}");
        }

        return BuildPeriodRows(now, list.Count, periodStart =>
        {
            var filtered = FilterIncome(list, periodStart).ToList();
            return (
                filtered.Count,
                filtered.Where(r => r.IncomeType == "REALIZED_PNL").Sum(r => ParseIncome(r.Income)),
                filtered.Where(r => r.IncomeType == "COMMISSION").Sum(r => Math.Abs(ParseIncome(r.Income))));
        }, log, "[trade-stats] income");
    }

    private static List<TradeStatsPeriodRow> BuildPeriodRows(
        DateTime now,
        int totalCount,
        Func<DateTime, (int Count, double Pnl, double Commission)> aggregate,
        Action<string>? log,
        string prefix)
    {
        if (totalCount == 0)
        {
            log?.Invoke($"{prefix} no rows — periods will be zero");
        }

        var rows = new List<TradeStatsPeriodRow>();
        foreach (var period in Periods)
        {
            var start = period.Start(now);
            var (count, pnl, commission) = aggregate(start);
            log?.Invoke($"{prefix} period «{period.Label}»: rows={count} pnl={pnl:F6} comm={commission:F6}");
            rows.Add(new TradeStatsPeriodRow
            {
                PeriodLabel = period.Label,
                RealizedPnl = pnl,
                Commission = commission,
            });
        }

        return rows;
    }

    private static IEnumerable<UserTradeModel> FilterTrades(IReadOnlyList<UserTradeModel> list, DateTime periodStart) =>
        periodStart == DateTime.MinValue
            ? list
            : list.Where(t => t.Time >= periodStart);

    private static IEnumerable<FuturesIncomeRecord> FilterIncome(IReadOnlyList<FuturesIncomeRecord> list, DateTime periodStart) =>
        periodStart == DateTime.MinValue
            ? list
            : list.Where(r => ToLocal(r.Time) >= periodStart);

    private static bool IsRelevantIncome(FuturesIncomeRecord row) =>
        row.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase)
        && row.IncomeType is "REALIZED_PNL" or "COMMISSION";

    private static DateTime ToLocal(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;

    private static double ParseIncome(string raw) =>
        double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
