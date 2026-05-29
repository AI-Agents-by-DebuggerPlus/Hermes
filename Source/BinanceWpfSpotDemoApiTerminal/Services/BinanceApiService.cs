using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BinanceWpfSpotDemoApiTerminal.Helpers;
using BinanceWpfSpotDemoApiTerminal.Models;

namespace BinanceWpfSpotDemoApiTerminal.Services
{
    public class BinanceApiService
    {
        private const string BaseUrl = "https://testnet.binance.vision";
        private readonly HttpClient _httpClient;
        private readonly Action<string> _logger;

        public string ApiKey { get; set; }
        public string SecretKey { get; set; }

        public BinanceApiService(Action<string> logger = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AntigravityBinanceTerminal/1.0");
            _logger = logger;
        }

        private void Log(string message)
        {
            _logger?.Invoke($"[REST] {DateTime.Now:HH:mm:ss.fff} | {message}");
        }

        // 1. Получение информации обо всех торговых парах биржи
        public async Task<List<SymbolInfo>> GetExchangeInfoAsync()
        {
            Log("Отправка GET /api/v3/exchangeInfo");
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/v3/exchangeInfo");
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Log($"Ошибка API: {response.StatusCode} - {content}");
                    return new List<SymbolInfo>();
                }
                var result = JsonSerializer.Deserialize<ExchangeInfoResponse>(content);
                Log($"Успешно загружено {result?.Symbols?.Count} торговых пар");
                return result?.Symbols ?? new List<SymbolInfo>();
            }
            catch (Exception ex)
            {
                Log($"Исключение в GetExchangeInfo: {ex.Message}");
                return new List<SymbolInfo>();
            }
        }

        // 2. Получение исторических свечей
        public async Task<List<Candle>> GetKlinesAsync(string symbol, string interval = "1h", int limit = 100)
        {
            Log($"Отправка GET /api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}");
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}");
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Log($"Ошибка API: {response.StatusCode} - {content}");
                    return new List<Candle>();
                }

                var rawKlines = JsonSerializer.Deserialize<List<List<JsonElement>>>(content);
                var candles = new List<Candle>();

                if (rawKlines != null)
                 {
                    foreach (var item in rawKlines)
                    {
                        if (item.Count >= 6)
                        {
                            candles.Add(new Candle
                            {
                                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).DateTime.ToLocalTime(),
                                Open = double.Parse(item[1].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                                High = double.Parse(item[2].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                                Low = double.Parse(item[3].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                                Close = double.Parse(item[4].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                                Volume = double.Parse(item[5].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64()).DateTime.ToLocalTime()
                            });
                        }
                    }
                }
                Log($"Успешно загружено {candles.Count} свечей для {symbol}");
                return candles;
            }
            catch (Exception ex)
            {
                Log($"Исключение в GetKlines: {ex.Message}");
                return new List<Candle>();
            }
        }

        // 3. Получение балансов аккаунта (требуется подпись)
        public async Task<List<BalanceModel>> GetBalancesAsync()
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                Log("Ошибка: API-ключи не установлены. Загрузка балансов невозможна.");
                return new List<BalanceModel>();
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string query = $"timestamp={timestamp}";
            string signature = CryptoHelper.GenerateSignature(query, SecretKey);
            string url = $"{BaseUrl}/api/v3/account?{query}&signature={signature}";

            Log("Отправка GET /api/v3/account (авторизованный запрос)");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("X-MBX-APIKEY", ApiKey);
                    var response = await _httpClient.SendAsync(request);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Ошибка балансов аккаунта: {response.StatusCode} - {content}");
                        return new List<BalanceModel>();
                    }

                    var accountInfo = JsonSerializer.Deserialize<AccountInfoResponse>(content);
                    var balances = new List<BalanceModel>();
                    if (accountInfo?.Balances != null)
                    {
                        foreach (var raw in accountInfo.Balances)
                        {
                            double free = double.Parse(raw.Free, System.Globalization.CultureInfo.InvariantCulture);
                            double locked = double.Parse(raw.Locked, System.Globalization.CultureInfo.InvariantCulture);
                            if (free > 0 || locked > 0)
                            {
                                balances.Add(new BalanceModel
                                {
                                    Asset = raw.Asset,
                                    Free = free,
                                    Locked = locked
                                });
                            }
                        }
                    }
                    Log($"Загружено {balances.Count} ненулевых балансов");
                    return balances;
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение в GetBalances: {ex.Message}");
                return new List<BalanceModel>();
            }
        }

        // 4. Размещение ордера (покупки или продажи, требуется подпись)
        public async Task<BinanceOrder> PlaceOrderAsync(string symbol, string side, string type, double quantity, double? price = null)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                Log("Ошибка: API-ключи не установлены. Размещение ордера невозможно.");
                throw new InvalidOperationException("API credentials are not set.");
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sb = new StringBuilder();
            sb.Append($"symbol={symbol.ToUpper()}");
            sb.Append($"&side={side.ToUpper()}");
            sb.Append($"&type={type.ToUpper()}");
            sb.Append($"&quantity={quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (type.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                if (price == null)
                    throw new ArgumentException("Цена обязательна для лимитных (LIMIT) ордеров");
                
                sb.Append($"&price={price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                sb.Append("&timeInForce=GTC"); // Good 'Til Cancelled
            }
            sb.Append($"&timestamp={timestamp}");

            string query = sb.ToString();
            string signature = CryptoHelper.GenerateSignature(query, SecretKey);
            string url = $"{BaseUrl}/api/v3/order";

            Log($"Размещение ордера POST /api/v3/order?{query}");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Add("X-MBX-APIKEY", ApiKey);
                    request.Content = new StringContent($"{query}&signature={signature}", Encoding.UTF8, "application/x-www-form-urlencoded");
                    var response = await _httpClient.SendAsync(request);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Ошибка размещения ордера: {response.StatusCode} - {content}");
                        throw new Exception($"API Error: {content}");
                    }

                    Log($"Ордер успешно размещен! Пара={symbol}, Сторона={side}, Кол-во={quantity}");
                    return JsonSerializer.Deserialize<BinanceOrder>(content);
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение в PlaceOrder: {ex.Message}");
                throw;
            }
        }

        // 5. Отмена открытого ордера (требуется подпись)
        public async Task<BinanceOrder> CancelOrderAsync(string symbol, long orderId)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                Log("Ошибка: API-ключи не установлены. Отмена ордера невозможно.");
                return null;
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string query = $"symbol={symbol.ToUpper()}&orderId={orderId}&timestamp={timestamp}";
            string signature = CryptoHelper.GenerateSignature(query, SecretKey);
            string url = $"{BaseUrl}/api/v3/order";

            Log($"Отмена ордера DELETE /api/v3/order для {symbol} ID {orderId}");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Delete, url))
                {
                    request.Headers.Add("X-MBX-APIKEY", ApiKey);
                    request.Content = new StringContent($"{query}&signature={signature}", Encoding.UTF8, "application/x-www-form-urlencoded");
                    var response = await _httpClient.SendAsync(request);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Ошибка отмены ордера: {response.StatusCode} - {content}");
                        return null;
                    }

                    Log($"Ордер {orderId} успешно отменен");
                    return JsonSerializer.Deserialize<BinanceOrder>(content);
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение в CancelOrder: {ex.Message}");
                return null;
            }
        }

        // 6. Получение списка открытых ордеров (требуется подпись)
        public async Task<List<BinanceOrder>> GetOpenOrdersAsync(string symbol = null)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                Log("Ошибка: API-ключи не установлены. Запрос открытых ордеров невозможен.");
                return new List<BinanceOrder>();
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string query = string.IsNullOrEmpty(symbol) ? $"timestamp={timestamp}" : $"symbol={symbol.ToUpper()}&timestamp={timestamp}";
            string signature = CryptoHelper.GenerateSignature(query, SecretKey);
            string url = $"{BaseUrl}/api/v3/openOrders?{query}&signature={signature}";

            Log($"Запрос открытых ордеров GET /api/v3/openOrders?{query}");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("X-MBX-APIKEY", ApiKey);
                    var response = await _httpClient.SendAsync(request);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Ошибка открытых ордеров: {response.StatusCode} - {content}");
                        return new List<BinanceOrder>();
                    }

                    return JsonSerializer.Deserialize<List<BinanceOrder>>(content) ?? new List<BinanceOrder>();
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение в GetOpenOrders: {ex.Message}");
                return new List<BinanceOrder>();
            }
        }

        // 7. Получение истории всех ордеров (требуется подпись)
        public async Task<List<BinanceOrder>> GetOrderHistoryAsync(string symbol)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
            {
                Log("Ошибка: API-ключи не установлены. Запрос истории ордеров невозможен.");
                return new List<BinanceOrder>();
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string query = $"symbol={symbol.ToUpper()}&limit=50&timestamp={timestamp}";
            string signature = CryptoHelper.GenerateSignature(query, SecretKey);
            string url = $"{BaseUrl}/api/v3/allOrders?{query}&signature={signature}";

            Log($"Запрос истории ордеров GET /api/v3/allOrders?{query}");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("X-MBX-APIKEY", ApiKey);
                    var response = await _httpClient.SendAsync(request);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Ошибка истории ордеров: {response.StatusCode} - {content}");
                        return new List<BinanceOrder>();
                    }

                    return JsonSerializer.Deserialize<List<BinanceOrder>>(content) ?? new List<BinanceOrder>();
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение в GetOrderHistory: {ex.Message}");
                return new List<BinanceOrder>();
            }
        }
    }
}
