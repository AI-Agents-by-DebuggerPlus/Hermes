using System.Globalization;
using System.Text.Json;

namespace Hermes.TradingPlatform.Exchange.MarketData;

internal static class BinanceFuturesStreamParser
{
    public static bool TryParseMessage(string json, out BinanceStreamMessage message)
    {
        message = default!;
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
                "24hrTicker" => TryParse24hrTicker(payload, out message),
                "bookTicker" => TryParseBookTicker(payload, out message),
                "aggTrade" => TryParseAggTrade(payload, out message),
                "24hrMiniTicker" => TryParseMiniTicker(payload, out message),
                "kline" => TryParseKline(payload, out message),
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseTickerMessage(string json, out BinanceTickerUpdate update)
    {
        update = default!;
        if (!TryParseMessage(json, out var message))
        {
            return false;
        }

        if (message is not BinanceTickerStreamMessage ticker)
        {
            return false;
        }

        update = ticker.Update;
        return true;
    }

    private static bool TryParse24hrTicker(JsonElement payload, out BinanceStreamMessage message)
    {
        message = default!;
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

        message = new BinanceTickerStreamMessage(
            new BinanceTickerUpdate(symbol, price, changePercent, quoteVolume));
        return true;
    }

    private static bool TryParseBookTicker(JsonElement payload, out BinanceStreamMessage message)
    {
        message = default!;
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
        message = new BinanceTickerStreamMessage(new BinanceTickerUpdate(symbol, mid, 0m, 0m));
        return true;
    }

    private static bool TryParseAggTrade(JsonElement payload, out BinanceStreamMessage message)
    {
        message = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var price = ParseDecimal(payload, "p");
        var quantity = ParseDecimal(payload, "q");
        if (price <= 0)
        {
            return false;
        }

        var tradeId = payload.TryGetProperty("a", out var aProp) && aProp.TryGetInt64(out var id)
            ? id
            : 0L;
        var tradeTime = payload.TryGetProperty("T", out var tProp) && tProp.TryGetInt64(out var unixMs)
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
            : DateTimeOffset.UtcNow;

        // "m" = true → buyer is the market maker → aggressor was a SELL.
        var buyerIsMaker = payload.TryGetProperty("m", out var mProp) && mProp.GetBoolean();
        var aggressorBuy = !buyerIsMaker;

        message = new BinanceAggTradeStreamMessage(symbol, price, quantity, aggressorBuy, tradeId, tradeTime);
        return true;
    }

    private static bool TryParseMiniTicker(JsonElement payload, out BinanceStreamMessage message)
    {
        message = default!;
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

        message = new BinanceTickerStreamMessage(new BinanceTickerUpdate(symbol, price, 0m, 0m));
        return true;
    }

    private static bool TryParseKline(JsonElement payload, out BinanceStreamMessage message)
    {
        message = default!;
        var symbol = payload.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        if (!payload.TryGetProperty("k", out var k))
        {
            return false;
        }

        var interval = k.TryGetProperty("i", out var iProp) ? iProp.GetString() ?? "1m" : "1m";
        var open = ParseDecimal(k, "o");
        var high = ParseDecimal(k, "h");
        var low = ParseDecimal(k, "l");
        var close = ParseDecimal(k, "c");
        var volume = ParseDecimal(k, "v");
        var quoteVolume = ParseDecimal(k, "q");
        if (close <= 0 || open <= 0)
        {
            return false;
        }

        var openTime = k.TryGetProperty("t", out var tProp) && tProp.TryGetInt64(out var ot)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ot)
            : DateTimeOffset.UtcNow;
        var closeTime = k.TryGetProperty("T", out var capT) && capT.TryGetInt64(out var ct)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ct)
            : openTime;

        var trades = k.TryGetProperty("n", out var nProp) && nProp.TryGetInt32(out var nv) ? nv : 0;
        var isClosed = k.TryGetProperty("x", out var xProp) && xProp.GetBoolean();

        message = new BinanceKlineStreamMessage(
            symbol,
            interval,
            openTime,
            closeTime,
            open,
            high,
            low,
            close,
            volume,
            quoteVolume,
            trades,
            isClosed);
        return true;
    }

    private static decimal ParseDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) &&
        decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
}

internal abstract record BinanceStreamMessage;

internal sealed record BinanceTickerStreamMessage(BinanceTickerUpdate Update) : BinanceStreamMessage;

internal sealed record BinanceAggTradeStreamMessage(
    string Symbol,
    decimal Price,
    decimal Quantity,
    bool AggressorBuy,
    long TradeId,
    DateTimeOffset TradeTime) : BinanceStreamMessage;

internal sealed record BinanceKlineStreamMessage(
    string Symbol,
    string Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    bool IsClosed) : BinanceStreamMessage;

internal readonly record struct BinanceTickerUpdate(
    string Symbol,
    decimal LastPrice,
    decimal ChangePercent24h,
    decimal QuoteVolume24h);
