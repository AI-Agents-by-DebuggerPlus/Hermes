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

            string? eventType = null;
            if (payload.TryGetProperty("e", out var eventTypeProp))
            {
                eventType = eventTypeProp.GetString();
            }

            return eventType switch
            {
                "24hrTicker" => TryParse24hrTicker(payload, out update),
                "bookTicker" => TryParseBookTicker(payload, out update),
                "aggTrade" => TryParseAggTrade(payload, out update),
                "24hrMiniTicker" => TryParseMiniTicker(payload, out update),
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParse24hrTicker(JsonElement payload, out BinanceTickerUpdate update)
    {
        update = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
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

    private static bool TryParseBookTicker(JsonElement payload, out BinanceTickerUpdate update)
    {
        update = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var bid = ParseDecimal(payload, "b");
        var ask = ParseDecimal(payload, "a");
        if (bid <= 0 || ask <= 0)
        {
            return false;
        }

        var mid = (bid + ask) / 2m;
        update = new BinanceTickerUpdate(symbol, mid, 0m, 0m);
        return true;
    }

    private static bool TryParseAggTrade(JsonElement payload, out BinanceTickerUpdate update)
    {
        update = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var price = ParseDecimal(payload, "p");
        if (price <= 0)
        {
            return false;
        }

        update = new BinanceTickerUpdate(symbol, price, 0m, 0m);
        return true;
    }

    private static bool TryParseMiniTicker(JsonElement payload, out BinanceTickerUpdate update)
    {
        update = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var price = ParseDecimal(payload, "c");
        if (price <= 0)
        {
            return false;
        }

        update = new BinanceTickerUpdate(symbol, price, 0m, 0m);
        return true;
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
