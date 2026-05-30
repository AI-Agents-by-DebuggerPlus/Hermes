using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hermes.BinanceDemoSpotTerminal.Models
{
    // Информация об аккаунте
    public class AccountInfoResponse
    {
        [JsonPropertyName("canTrade")]
        public bool CanTrade { get; set; }

        [JsonPropertyName("updateTime")]
        public long UpdateTime { get; set; }

        [JsonPropertyName("accountType")]
        public string AccountType { get; set; }

        [JsonPropertyName("balances")]
        public List<RawBalance> Balances { get; set; }
    }

    public class RawBalance
    {
        [JsonPropertyName("asset")]
        public string Asset { get; set; }

        [JsonPropertyName("free")]
        public string Free { get; set; }

        [JsonPropertyName("locked")]
        public string Locked { get; set; }
    }

    // Обработанный баланс для UI
    public class BalanceModel
    {
        public string Asset { get; set; }
        public double Free { get; set; }
        public double Locked { get; set; }
        public double Total => Free + Locked;

        public string FreeDisplay => Free.ToString("N8").TrimEnd('0').TrimEnd('.', ',');
        public string LockedDisplay => Locked.ToString("N8").TrimEnd('0').TrimEnd('.', ',');
        public string TotalDisplay => Total.ToString("N8").TrimEnd('0').TrimEnd('.', ',');
    }

    // Ответ API по ордеру (Limit/Market ордер)
    public class BinanceOrder
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("orderId")]
        public long OrderId { get; set; }

        [JsonPropertyName("clientOrderId")]
        public string ClientOrderId { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; }

        [JsonPropertyName("origQty")]
        public string OrigQty { get; set; }

        [JsonPropertyName("executedQty")]
        public string ExecutedQty { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("timeInForce")]
        public string TimeInForce { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("side")]
        public string Side { get; set; }

        // Время создания ордера может приходить как "time", так и в transactTime
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("transactTime")]
        public long TransactTime { get; set; }

        [JsonPropertyName("updateTime")]
        public long UpdateTime { get; set; }
    }

    // Ордер для UI
    public class OrderModel
    {
        public long OrderId { get; set; }
        public string Symbol { get; set; }
        public DateTime Time { get; set; }
        public string Side { get; set; } // BUY or SELL
        public string Type { get; set; } // LIMIT or MARKET
        public double Price { get; set; }
        public double OrigQty { get; set; }
        public double ExecutedQty { get; set; }
        public string Status { get; set; }

        public string TimeDisplay => Time.ToString("yyyy-MM-dd HH:mm:ss");
        public string PriceDisplay => Price > 0 ? Price.ToString("N4") : "MARKET";
        public string AmountDisplay => OrigQty.ToString("N4");
        public string ExecutedDisplay => ExecutedQty.ToString("N4");
        public bool IsBuy => Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        public bool CanCancel => Status.Equals("NEW", StringComparison.OrdinalIgnoreCase) || 
                                 Status.Equals("PARTIALLY_FILLED", StringComparison.OrdinalIgnoreCase);
    }
}
