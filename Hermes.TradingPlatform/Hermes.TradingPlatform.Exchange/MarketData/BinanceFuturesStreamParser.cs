using System.Globalization;
using System.Text.Json;

namespace Hermes.TradingPlatform.Exchange.MarketData;

internal static class BinanceFuturesStreamParser
{
    public static bool TryParseTickerMessage(string json, out BinanceTickerUpdate update)
    {
        update = default!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var payload = root.TryGetProperty("data", out var data) ? data : root;
            if (!payload.TryGetProperty("e", out var eventType) ||
                eventType.GetString() is not "24hrTicker")
            {
                return false;
            }

            var symbol = payload.GetProperty("s").GetString();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            var price = ParseDecimal(payload, "c");
            var changePercent = ParseDecimal(payload, "P");
            var quoteVolume = ParseDecimal(payload, "q");
            if (price <= 0)
            {
                return false;
            }

            update = new BinanceTickerUpdate(symbol, price, changePercent, quoteVolume);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static decimal ParseDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) &&
        decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
}

internal readonly record struct BinanceTickerUpdate(
    string Symbol,
    decimal LastPrice,
    decimal ChangePercent24h,
    decimal QuoteVolume24h);
