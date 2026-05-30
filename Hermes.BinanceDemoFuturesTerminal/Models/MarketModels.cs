using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hermes.BinanceDemoFuturesTerminal.Models
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

        [JsonPropertyName("contractType")]
        public string ContractType { get; set; }

        [JsonPropertyName("baseAssetPrecision")]
        public int BaseAssetPrecision { get; set; }

        [JsonPropertyName("quotePrecision")]
        public int QuotePrecision { get; set; }

        [JsonPropertyName("quantityPrecision")]
        public int QuantityPrecision { get; set; }

        [JsonPropertyName("filters")]
        public List<SymbolFilter> Filters { get; set; }

        public double GetStepSize()
        {
            var lot = GetLotSizeFilter();
            if (lot != null && double.TryParse(lot.StepSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var step) && step > 0)
            {
                return step;
            }

            var precision = QuantityPrecision > 0 ? QuantityPrecision : BaseAssetPrecision;
            return Math.Pow(10, -Math.Max(0, precision));
        }

        public double GetMinQty()
        {
            var lot = GetLotSizeFilter();
            if (lot != null && double.TryParse(lot.MinQty, NumberStyles.Any, CultureInfo.InvariantCulture, out var min) && min > 0)
            {
                return min;
            }

            return GetStepSize();
        }

        public double GetMinNotional()
        {
            var filter = Filters?.FirstOrDefault(f =>
                string.Equals(f.FilterType, "MIN_NOTIONAL", StringComparison.OrdinalIgnoreCase));
            if (filter?.Notional != null
                && double.TryParse(filter.Notional, NumberStyles.Any, CultureInfo.InvariantCulture, out var min)
                && min > 0)
            {
                return min;
            }

            return 0;
        }

        public double GetMinOrderUsdt(double price)
        {
            if (price <= 0)
            {
                return 0;
            }

            return Math.Max(GetMinQty() * price, GetMinNotional());
        }

        public string GetDefaultQuantityInput(bool quantityInUsdt, double price)
        {
            var minQty = GetMinQty();
            if (quantityInUsdt)
            {
                if (price <= 0)
                {
                    return string.Empty;
                }

                var minUsdt = GetMinOrderUsdt(price);
                return minUsdt.ToString("F2", CultureInfo.InvariantCulture);
            }

            return FormatQuantity(minQty);
        }

        public decimal RoundQuantityDecimal(decimal qty)
        {
            var lot = GetLotSizeFilter();
            if (lot?.StepSize == null
                || !decimal.TryParse(lot.StepSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var step)
                || step <= 0)
            {
                return qty;
            }

            return Math.Floor(qty / step) * step;
        }

        public double RoundQuantity(double qty) => (double)RoundQuantityDecimal((decimal)qty);

        public string FormatQuantity(double qty)
        {
            var rounded = RoundQuantityDecimal((decimal)qty);
            var decimals = GetStepDecimalPlaces(GetLotSizeFilter()?.StepSize);
            return rounded.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        public string FormatPrice(double price)
        {
            var tickFilter = Filters?.FirstOrDefault(f =>
                string.Equals(f.FilterType, "PRICE_FILTER", StringComparison.OrdinalIgnoreCase));
            if (tickFilter?.TickSize != null
                && decimal.TryParse(tickFilter.TickSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var tick)
                && tick > 0)
            {
                var rounded = Math.Floor((decimal)price / tick) * tick;
                var decimals = GetStepDecimalPlaces(tickFilter.TickSize);
                return rounded.ToString($"F{decimals}", CultureInfo.InvariantCulture);
            }

            var precision = QuotePrecision > 0 ? QuotePrecision : 2;
            return ((decimal)price).ToString($"F{precision}", CultureInfo.InvariantCulture);
        }

        private SymbolFilter? GetLotSizeFilter() =>
            Filters?.FirstOrDefault(f =>
                string.Equals(f.FilterType, "LOT_SIZE", StringComparison.OrdinalIgnoreCase));

        private static int GetStepDecimalPlaces(string? stepSize)
        {
            if (string.IsNullOrWhiteSpace(stepSize))
            {
                return 8;
            }

            var normalized = stepSize.Trim();
            var dot = normalized.IndexOf('.');
            if (dot < 0)
            {
                return 0;
            }

            return normalized.Length - dot - 1;
        }
    }

    public class SymbolFilter
    {
        [JsonPropertyName("filterType")]
        public string FilterType { get; set; }

        [JsonPropertyName("stepSize")]
        public string StepSize { get; set; }

        [JsonPropertyName("minQty")]
        public string MinQty { get; set; }

        [JsonPropertyName("notional")]
        public string Notional { get; set; }

        [JsonPropertyName("tickSize")]
        public string TickSize { get; set; }
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
