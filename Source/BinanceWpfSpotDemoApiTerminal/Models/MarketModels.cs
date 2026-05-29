using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BinanceWpfSpotDemoApiTerminal.Models
{
    // Информация о торговых парах биржи
    public class ExchangeInfoResponse
    {
        [JsonPropertyName("symbols")]
        public List<SymbolInfo> Symbols { get; set; }
    }

    public class SymbolInfo
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("baseAsset")]
        public string BaseAsset { get; set; }

        [JsonPropertyName("quoteAsset")]
        public string QuoteAsset { get; set; }

        [JsonPropertyName("baseAssetPrecision")]
        public int BaseAssetPrecision { get; set; }

        [JsonPropertyName("quotePrecision")]
        public int QuotePrecision { get; set; }
    }

    // 24-часовая статистика по тикеру
    public class Ticker24Hr
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("priceChangePercent")]
        public string PriceChangePercent { get; set; }

        [JsonPropertyName("lastPrice")]
        public string LastPrice { get; set; }

        [JsonPropertyName("highPrice")]
        public string HighPrice { get; set; }

        [JsonPropertyName("lowPrice")]
        public string LowPrice { get; set; }

        [JsonPropertyName("volume")]
        public string Volume { get; set; }
    }

    // Модель для хранения обработанных данных тикера в UI
    public class TickerModel
    {
        public string Symbol { get; set; }
        public double LastPrice { get; set; }
        public double PriceChangePercent { get; set; }
        public double HighPrice { get; set; }
        public double LowPrice { get; set; }
        public double Volume { get; set; }

        public string PriceDisplay => LastPrice.ToString("N4");
        public string PercentDisplay => (PriceChangePercent >= 0 ? "+" : "") + PriceChangePercent.ToString("F2") + "%";
        public bool IsPositive => PriceChangePercent >= 0;
    }

    // Стакан ордеров (глубина рынка)
    public class OrderBookResponse
    {
        [JsonPropertyName("lastUpdateId")]
        public long LastUpdateId { get; set; }

        [JsonPropertyName("bids")]
        public List<List<string>> Bids { get; set; }

        [JsonPropertyName("asks")]
        public List<List<string>> Asks { get; set; }
    }

    // Элемент стакана ордеров для отображения в UI
    public class OrderBookItem
    {
        public double Price { get; set; }
        public double Amount { get; set; }
        public double Total { get; set; }
        public double Percentage { get; set; } // Для визуального бара заполнения глубины

        public string PriceDisplay => Price.ToString("N4");
        public string AmountDisplay => Amount.ToString("N4");
        public string TotalDisplay => Total.ToString("N2");
    }

    // Недавняя сделка
    public class RecentTradeResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; }

        [JsonPropertyName("qty")]
        public string Qty { get; set; }

        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("isBuyerMaker")]
        public bool IsBuyerMaker { get; set; }
    }

    // Сделка для UI
    public class TradeModel
    {
        public DateTime Time { get; set; }
        public double Price { get; set; }
        public double Amount { get; set; }
        public bool IsBuy { get; set; } // Покупка или продажа (по рынку)

        public string TimeDisplay => Time.ToString("HH:mm:ss");
        public string PriceDisplay => Price.ToString("N4");
        public string AmountDisplay => Amount.ToString("N4");
    }

    // Японская свеча
    public class Candle
    {
        public DateTime OpenTime { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        public DateTime CloseTime { get; set; }
    }
}
