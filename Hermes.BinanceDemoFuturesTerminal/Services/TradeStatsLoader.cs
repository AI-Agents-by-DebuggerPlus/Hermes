using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public sealed class TradeStatsLoader
{
    private readonly BinanceApiService _api;
    private readonly Action<string> _log;

    public TradeStatsLoader(BinanceApiService api, Action<string>? log = null)
    {
        _api = api;
        _log = log ?? (_ => { });
    }

    public async Task<TradeStatsLoadResult> LoadAsync(
        IEnumerable<string> seedSymbols,
        int lookbackDays = 365,
        CancellationToken ct = default)
    {
        var startTimeMs = DateTimeOffset.UtcNow.AddDays(-lookbackDays).ToUnixTimeMilliseconds();
        var incomeStartMs = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeMilliseconds();
        var seedList = seedSymbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _log($"[trade-stats] load start seeds=[{string.Join(", ", seedList)}] lookback={lookbackDays}d");

        var symbols = new HashSet<string>(seedList, StringComparer.OrdinalIgnoreCase);
        _log($"[trade-stats] GET /fapi/v1/income from {FormatMs(incomeStartMs)}");
        var incomeRows = await _api.GetIncomeSinceAsync(incomeStartMs, ct: ct).ConfigureAwait(false);
        LogIncomeSummary(incomeRows);

        foreach (var row in incomeRows)
        {
            if (!string.IsNullOrWhiteSpace(row.Symbol))
            {
                symbols.Add(row.Symbol);
            }
        }

        _log($"[trade-stats] symbols after income: [{string.Join(", ", symbols.OrderBy(s => s))}] count={symbols.Count}");

        if (symbols.Count == 0)
        {
            _log("[trade-stats] WARN no symbols — cannot query userTrades");
            return new TradeStatsLoadResult([], incomeRows, 0, incomeRows.Count);
        }

        var tradesById = new Dictionary<long, UserTradeModel>();
        foreach (var symbol in symbols.OrderBy(s => s))
        {
            ct.ThrowIfCancellationRequested();
            _log($"[trade-stats] GET /fapi/v1/userTrades {symbol} from {FormatMs(startTimeMs)}");
            var rows = await _api.GetUserTradesSinceAsync(symbol, startTimeMs, ct: ct).ConfigureAwait(false);
            var pnlSum = rows.Sum(r => ParseDouble(r.RealizedPnl));
            var commSum = rows.Sum(r => ParseDouble(r.Commission));
            _log(
                $"[trade-stats] userTrades {symbol}: fills={rows.Count} pnl={pnlSum:F6} comm={commSum:F6}");
            if (rows.Count > 0)
            {
                var last = rows.OrderByDescending(r => r.Time).First();
                _log(
                    $"[trade-stats] userTrades {symbol} last: id={last.Id} time={FormatMs(last.Time)} "
                    + $"pnl={last.RealizedPnl} comm={last.Commission} {last.CommissionAsset}");
            }

            foreach (var row in rows)
            {
                tradesById[row.Id] = MapTrade(row);
            }
        }

        _log($"[trade-stats] load done uniqueFills={tradesById.Count} incomeRows={incomeRows.Count}");
        return new TradeStatsLoadResult(tradesById.Values.ToList(), incomeRows, symbols.Count, incomeRows.Count);
    }

    private void LogIncomeSummary(IReadOnlyList<FuturesIncomeRecord> incomeRows)
    {
        _log($"[trade-stats] income total rows={incomeRows.Count}");
        if (incomeRows.Count == 0)
        {
            _log("[trade-stats] income empty response");
            return;
        }

        var byType = incomeRows
            .GroupBy(r => r.IncomeType)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToList();
        _log($"[trade-stats] income by type: {string.Join(", ", byType)}");

        var pnl = incomeRows
            .Where(r => r.IncomeType == "REALIZED_PNL" && r.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            .Sum(r => ParseDouble(r.Income));
        var comm = incomeRows
            .Where(r => r.IncomeType == "COMMISSION" && r.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            .Sum(r => Math.Abs(ParseDouble(r.Income)));
        _log($"[trade-stats] income totals: pnl={pnl:F6} comm={comm:F6} USDT");
    }

    private static string FormatMs(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static double ParseDouble(string? raw) =>
        double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static UserTradeModel MapTrade(UserTradeResponse raw) =>
        new()
        {
            TradeId = raw.Id,
            OrderId = raw.OrderId,
            Time = DateTimeOffset.FromUnixTimeMilliseconds(raw.Time).DateTime.ToLocalTime(),
            Symbol = raw.Symbol,
            ContractBadge = string.Empty,
            IsBuy = raw.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase),
            Price = ParseDouble(raw.Price),
            QuoteQty = ParseDouble(raw.QuoteQty),
            Commission = ParseDouble(raw.Commission),
            CommissionAsset = raw.CommissionAsset,
            IsMaker = raw.Maker,
            RealizedPnl = ParseDouble(raw.RealizedPnl),
        };
}

public sealed class TradeStatsLoadResult
{
    public TradeStatsLoadResult(
        IReadOnlyList<UserTradeModel> trades,
        IReadOnlyList<FuturesIncomeRecord> incomeRecords,
        int symbolCount,
        int incomeRowCount)
    {
        Trades = trades;
        IncomeRecords = incomeRecords;
        SymbolCount = symbolCount;
        IncomeRowCount = incomeRowCount;
    }

    public IReadOnlyList<UserTradeModel> Trades { get; }
    public IReadOnlyList<FuturesIncomeRecord> IncomeRecords { get; }
    public int SymbolCount { get; }
    public int IncomeRowCount { get; }
}
