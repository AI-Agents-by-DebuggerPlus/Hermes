using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Hermes.StrategyViewer.Services;

public sealed class MarketSnapshot
{
    public string ApiSymbol { get; init; } = "XAUUSDT";
    public string SourceLabel { get; init; } = "Binance Futures API (XAUUSDT)";
    public string SourceUrl { get; init; } = "https://fapi.binance.com/fapi/v1/ticker/24hr?symbol=XAUUSDT";
    public double LastPrice { get; init; }
    public double ChangeAbs { get; init; }
    public double ChangePct { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public double? Rsi14Daily { get; init; }
    public double? StochKDaily { get; init; }
    public double? Ema10Daily { get; init; }
    public double? LastClosedH1 { get; init; }
    public string? FetchError { get; init; }
}

public sealed class BinanceMarketDataService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly StrategyLogService _log;

    public BinanceMarketDataService(StrategyLogService log)
    {
        _log = log;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermes.StrategyViewer/0.1");
    }

    public async Task<MarketSnapshot?> FetchGoldSnapshotAsync(CancellationToken ct = default)
    {
        const string symbol = "XAUUSDT";
        try
        {
            var tickerUrl = $"https://fapi.binance.com/fapi/v1/ticker/24hr?symbol={symbol}";
            _log.Info($"GET {tickerUrl}");
            var tickerJson = await _http.GetStringAsync(tickerUrl, ct).ConfigureAwait(false);
            using var tickerDoc = JsonDocument.Parse(tickerJson);
            var root = tickerDoc.RootElement;
            var last = ParseDouble(root, "lastPrice");
            var changePct = ParseDouble(root, "priceChangePercent");
            var changeAbs = ParseDouble(root, "priceChange");

            var daily = await FetchKlinesAsync(symbol, "1d", 120, ct).ConfigureAwait(false);
            var h1 = await FetchKlinesAsync(symbol, "1h", 48, ct).ConfigureAwait(false);

            var closesD = daily.Select(k => k.Close).ToList();
            var highsD = daily.Select(k => k.High).ToList();
            var lowsD = daily.Select(k => k.Low).ToList();

            double? lastH1Close = null;
            if (h1.Count >= 2)
            {
                lastH1Close = h1[^2].Close;
            }

            var snap = new MarketSnapshot
            {
                ApiSymbol = symbol,
                LastPrice = last,
                ChangeAbs = changeAbs,
                ChangePct = changePct,
                UpdatedAt = DateTimeOffset.Now,
                Rsi14Daily = TechnicalIndicators.Rsi(closesD, 14),
                StochKDaily = TechnicalIndicators.StochasticK(highsD, lowsD, closesD, 14),
                Ema10Daily = TechnicalIndicators.Ema(closesD, 10),
                LastClosedH1 = lastH1Close,
            };

            _log.Info(
                $"Market OK {symbol} last={last:F2} Δ={changeAbs:F2} ({changePct:F2}%) "
                + $"RSI={snap.Rsi14Daily:F1} Stoch={snap.StochKDaily:F1} EMA10={snap.Ema10Daily:F2}");

            return snap;
        }
        catch (Exception ex)
        {
            _log.Error("Binance market fetch failed", ex);
            return new MarketSnapshot
            {
                FetchError = ex.Message,
                UpdatedAt = DateTimeOffset.Now,
            };
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<List<Kline>> FetchKlinesAsync(string symbol, string interval, int limit, CancellationToken ct)
    {
        var url =
            $"https://fapi.binance.com/fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}";
        _log.Info($"GET {url}");
        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var list = new List<Kline>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            list.Add(new Kline
            {
                Open = ParseIndex(row, 1),
                High = ParseIndex(row, 2),
                Low = ParseIndex(row, 3),
                Close = ParseIndex(row, 4),
            });
        }

        return list;
    }

    private static double ParseDouble(JsonElement el, string name) =>
        double.Parse(el.GetProperty(name).GetString() ?? "0", CultureInfo.InvariantCulture);

    private static double ParseIndex(JsonElement row, int index) =>
        double.Parse(row[index].GetString() ?? "0", CultureInfo.InvariantCulture);

    private sealed class Kline
    {
        public double Open { get; init; }
        public double High { get; init; }
        public double Low { get; init; }
        public double Close { get; init; }
    }
}
