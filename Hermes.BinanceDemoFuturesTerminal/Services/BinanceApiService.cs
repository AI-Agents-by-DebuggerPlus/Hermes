using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hermes.BinanceDemoFuturesTerminal.Helpers;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public sealed class BinanceApiService
{
    private const string BaseUrl = "https://demo-fapi.binance.com";
    private readonly HttpClient _httpClient;
    private readonly Action<string>? _logger;

    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public BinanceApiService(Action<string>? logger = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HermesBinanceDemoFuturesTerminal/1.0");
        _logger = logger;
    }

    private void Log(string message) =>
        _logger?.Invoke($"[REST] {DateTime.Now:HH:mm:ss.fff} | {message}");

    public async Task<List<SymbolInfo>> GetExchangeInfoAsync()
    {
        Log("GET /fapi/v1/exchangeInfo");
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/fapi/v1/exchangeInfo").ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Ошибка API: {response.StatusCode} - {content}");
                return [];
            }

            var result = JsonSerializer.Deserialize<ExchangeInfoResponse>(content);
            Log($"Загружено {result?.Symbols?.Count ?? 0} контрактов");
            return result?.Symbols ?? [];
        }
        catch (Exception ex)
        {
            Log($"GetExchangeInfo: {ex.Message}");
            return [];
        }
    }

    public async Task<List<Candle>> GetKlinesAsync(string symbol, string interval = "1m", int limit = 100)
    {
        Log($"GET /fapi/v1/klines symbol={symbol}");
        try
        {
            var response = await _httpClient
                .GetAsync($"{BaseUrl}/fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}")
                .ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Ошибка klines: {response.StatusCode} - {content}");
                return [];
            }

            var rawKlines = JsonSerializer.Deserialize<List<List<JsonElement>>>(content);
            var candles = new List<Candle>();
            if (rawKlines is null)
            {
                return candles;
            }

            foreach (var item in rawKlines)
            {
                if (item.Count < 7)
                {
                    continue;
                }

                candles.Add(new Candle
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).DateTime.ToLocalTime(),
                    Open = double.Parse(item[1].GetString()!, CultureInfo.InvariantCulture),
                    High = double.Parse(item[2].GetString()!, CultureInfo.InvariantCulture),
                    Low = double.Parse(item[3].GetString()!, CultureInfo.InvariantCulture),
                    Close = double.Parse(item[4].GetString()!, CultureInfo.InvariantCulture),
                    Volume = double.Parse(item[5].GetString()!, CultureInfo.InvariantCulture),
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64()).DateTime.ToLocalTime(),
                });
            }

            return candles;
        }
        catch (Exception ex)
        {
            Log($"GetKlines: {ex.Message}");
            return [];
        }
    }

    public async Task<WsDepthPayload?> GetDepthAsync(string symbol, int limit = 20)
    {
        Log($"GET /fapi/v1/depth symbol={symbol} limit={limit}");
        try
        {
            var response = await _httpClient
                .GetAsync($"{BaseUrl}/fapi/v1/depth?symbol={symbol.ToUpperInvariant()}&limit={limit}")
                .ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Ошибка depth: {response.StatusCode} - {content}");
                return null;
            }

            return JsonSerializer.Deserialize<WsDepthPayload>(content);
        }
        catch (Exception ex)
        {
            Log($"GetDepth: {ex.Message}");
            return null;
        }
    }

    public async Task<List<BalanceModel>> GetBalancesAsync()
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            Log("API-ключи не установлены");
            return [];
        }

        var url = SignedUrl("/fapi/v2/account", "timestamp={0}");
        Log("GET /fapi/v2/account");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Ошибка account: {response.StatusCode} - {content}");
                return [];
            }

            var accountInfo = JsonSerializer.Deserialize<AccountInfoResponse>(content);
            var balances = new List<BalanceModel>();
            foreach (var raw in accountInfo?.Assets ?? [])
            {
                var wallet = double.Parse(raw.WalletBalance, CultureInfo.InvariantCulture);
                var available = double.Parse(raw.AvailableBalance, CultureInfo.InvariantCulture);
                if (wallet <= 0 && available <= 0)
                {
                    continue;
                }

                balances.Add(new BalanceModel
                {
                    Asset = raw.Asset,
                    Free = available,
                    Locked = Math.Max(0, wallet - available),
                });
            }

            return balances;
        }
        catch (Exception ex)
        {
            Log($"GetBalances: {ex.Message}");
            return [];
        }
    }

    public async Task<List<PositionModel>> GetPositionsAsync(string? symbol = null)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return [];
        }

        var query = string.IsNullOrEmpty(symbol)
            ? "timestamp={0}"
            : $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}";
        var url = SignedUrl("/fapi/v2/positionRisk", query);
        Log("GET /fapi/v2/positionRisk");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Ошибка positions: {response.StatusCode} - {content}");
                return [];
            }

            var rows = JsonSerializer.Deserialize<List<PositionRiskResponse>>(content) ?? [];
            var positions = new List<PositionModel>();
            foreach (var row in rows)
            {
                var size = double.Parse(row.PositionAmt, CultureInfo.InvariantCulture);
                if (Math.Abs(size) < 1e-12)
                {
                    continue;
                }

                positions.Add(new PositionModel
                {
                    Symbol = row.Symbol,
                    Size = size,
                    EntryPrice = double.Parse(row.EntryPrice, CultureInfo.InvariantCulture),
                    MarkPrice = double.Parse(row.MarkPrice, CultureInfo.InvariantCulture),
                    UnrealizedPnl = double.Parse(row.UnRealizedProfit, CultureInfo.InvariantCulture),
                    Leverage = int.TryParse(row.Leverage, out var lev) ? lev : 1,
                    Side = size > 0 ? "LONG" : "SHORT",
                });
            }

            return positions;
        }
        catch (Exception ex)
        {
            Log($"GetPositions: {ex.Message}");
            return [];
        }
    }

    public async Task<BinanceOrder> PlaceOrderAsync(string symbol, string side, string type, string quantity, string? price = null)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            throw new InvalidOperationException("API credentials are not set.");
        }

        var sb = new StringBuilder();
        sb.Append($"symbol={symbol.ToUpperInvariant()}");
        sb.Append($"&side={side.ToUpperInvariant()}");
        sb.Append($"&type={type.ToUpperInvariant()}");
        sb.Append($"&quantity={quantity}");
        if (type.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Цена обязательна для LIMIT");
            }

            sb.Append($"&price={price}");
            sb.Append("&timeInForce=GTC");
        }

        var url = SignedUrl("/fapi/v1/order", sb.ToString() + "&timestamp={0}", usePost: true);
        Log($"POST /fapi/v1/order {side} {symbol}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/order");
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"PlaceOrder error: {content}");
            throw new Exception($"API Error: {content}");
        }

        return JsonSerializer.Deserialize<BinanceOrder>(content)
               ?? throw new Exception("Empty order response");
    }

    public async Task<BinanceOrder> PlaceConditionalOrderAsync(
        string symbol,
        string side,
        string type,
        string quantity,
        string stopPrice)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            throw new InvalidOperationException("API credentials are not set.");
        }

        var sb = new StringBuilder();
        sb.Append($"symbol={symbol.ToUpperInvariant()}");
        sb.Append($"&side={side.ToUpperInvariant()}");
        sb.Append($"&type={type.ToUpperInvariant()}");
        sb.Append($"&quantity={quantity}");
        sb.Append($"&stopPrice={stopPrice}");
        sb.Append("&reduceOnly=true");
        sb.Append("&workingType=CONTRACT_PRICE");

        var url = SignedUrl("/fapi/v1/order", sb.ToString() + "&timestamp={0}", usePost: true);
        Log($"POST /fapi/v1/order {type} {side} {symbol} stop={stopPrice}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/order");
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"ConditionalOrder error: {content}");
            throw new Exception($"API Error: {content}");
        }

        return JsonSerializer.Deserialize<BinanceOrder>(content)
               ?? throw new Exception("Empty conditional order response");
    }

    public async Task CancelConditionalOrdersAsync(string symbol, Func<string, bool> typeFilter)
    {
        var openOrders = await GetOpenOrdersAsync(symbol).ConfigureAwait(false);
        foreach (var order in openOrders.Where(o => typeFilter(o.Type)))
        {
            await CancelOrderAsync(symbol, order.OrderId).ConfigureAwait(false);
        }
    }

    public async Task<BinanceOrder?> CancelOrderAsync(string symbol, long orderId)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var query = $"symbol={symbol.ToUpperInvariant()}&orderId={orderId}&timestamp={timestamp}";
        var signature = CryptoHelper.GenerateSignature(query, SecretKey);
        Log($"DELETE /fapi/v1/order id={orderId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/fapi/v1/order");
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        request.Content = new StringContent($"{query}&signature={signature}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"CancelOrder error: {content}");
            return null;
        }

        return JsonSerializer.Deserialize<BinanceOrder>(content);
    }

    public async Task<List<BinanceOrder>> GetOpenOrdersAsync(string? symbol = null)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return [];
        }

        var query = string.IsNullOrEmpty(symbol)
            ? "timestamp={0}"
            : $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}";
        var url = SignedUrl("/fapi/v1/openOrders", query);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"OpenOrders error: {content}");
            return [];
        }

        return JsonSerializer.Deserialize<List<BinanceOrder>>(content) ?? [];
    }

    public async Task<List<BinanceOrder>> GetOrderHistoryAsync(string symbol)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return [];
        }

        var url = SignedUrl("/fapi/v1/allOrders", $"symbol={symbol.ToUpperInvariant()}&limit=50&timestamp={{0}}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"OrderHistory error: {content}");
            return [];
        }

        return JsonSerializer.Deserialize<List<BinanceOrder>>(content) ?? [];
    }

    private string SignedUrl(string path, string queryTemplate, bool usePost = false)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var query = string.Format(CultureInfo.InvariantCulture, queryTemplate, timestamp);
        var signature = CryptoHelper.GenerateSignature(query, SecretKey);
        return $"{BaseUrl}{path}?{query}&signature={signature}";
    }

    private static string ExtractBody(string signedUrl)
    {
        var idx = signedUrl.IndexOf('?');
        return idx >= 0 ? signedUrl[(idx + 1)..] : signedUrl;
    }
}
