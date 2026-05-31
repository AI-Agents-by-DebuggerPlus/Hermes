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

                var markPrice = double.Parse(row.MarkPrice, CultureInfo.InvariantCulture);
                var leverage = int.TryParse(row.Leverage, out var lev) ? lev : 1;
                var notional = Math.Abs(ParseDouble(row.Notional));
                if (notional <= 0)
                {
                    notional = Math.Abs(size) * markPrice;
                }

                var isolatedMargin = ParseDouble(row.IsolatedMargin);
                var marginType = FuturesMarginTypeExtensions.ParseApi(row.MarginType);
                var initialMargin = marginType == FuturesMarginType.Isolated && isolatedMargin > 0
                    ? isolatedMargin
                    : leverage > 0 ? notional / leverage : 0;

                positions.Add(new PositionModel
                {
                    Symbol = row.Symbol,
                    Size = size,
                    EntryPrice = double.Parse(row.EntryPrice, CultureInfo.InvariantCulture),
                    MarkPrice = markPrice,
                    UnrealizedPnl = double.Parse(row.UnRealizedProfit, CultureInfo.InvariantCulture),
                    Leverage = leverage,
                    Side = size > 0 ? "LONG" : "SHORT",
                    LiquidationPrice = ParseDouble(row.LiquidationPrice),
                    BreakEvenPrice = ParseDouble(row.BreakEvenPrice),
                    InitialMargin = initialMargin,
                    MaintMargin = ParseDouble(row.MaintMargin),
                    NotionalUsdt = notional,
                    MarginType = marginType,
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

    public async Task<int> GetSymbolLeverageAsync(string symbol)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol))
        {
            return 20;
        }

        var url = SignedUrl("/fapi/v2/positionRisk", $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return 20;
            }

            var rows = JsonSerializer.Deserialize<List<PositionRiskResponse>>(content) ?? [];
            var row = rows.FirstOrDefault(r =>
                r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            return row != null && int.TryParse(row.Leverage, out var lev) && lev > 0 ? lev : 20;
        }
        catch
        {
            return 20;
        }
    }

    public async Task<FuturesMarginType> GetSymbolMarginTypeAsync(string symbol)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol))
        {
            return FuturesMarginType.Cross;
        }

        var url = SignedUrl("/fapi/v2/positionRisk", $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return FuturesMarginType.Cross;
            }

            var rows = JsonSerializer.Deserialize<List<PositionRiskResponse>>(content) ?? [];
            var row = rows.FirstOrDefault(r =>
                r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            return FuturesMarginTypeExtensions.ParseApi(row?.MarginType);
        }
        catch
        {
            return FuturesMarginType.Cross;
        }
    }

    public async Task<(bool Success, string? Error)> SetMarginTypeAsync(string symbol, FuturesMarginType marginType)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol))
        {
            return (false, "API-ключи не заданы.");
        }

        var url = SignedUrl(
            "/fapi/v1/marginType",
            $"symbol={symbol.ToUpperInvariant()}&marginType={marginType.ToApiValue()}&timestamp={{0}}");
        Log($"POST /fapi/v1/marginType {symbol} {marginType.ToApiValue()}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/marginType");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            Log($"SetMarginType error: {content}");
            return (false, ParseApiErrorMessage(content));
        }
        catch (Exception ex)
        {
            Log($"SetMarginType: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public static bool IsMarginTypeUnchangedError(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && error.Contains("No need to change margin type", StringComparison.OrdinalIgnoreCase);

    private static string ParseApiErrorMessage(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("msg", out var msg))
            {
                return msg.GetString() ?? content;
            }
        }
        catch
        {
            // ignore parse errors
        }

        return content;
    }

    public async Task<List<LeverageBracket>> GetLeverageBracketsAsync(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            return [];
        }

        var query = string.IsNullOrEmpty(ApiKey)
            ? $"symbol={symbol.ToUpperInvariant()}"
            : $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}";

        var url = string.IsNullOrEmpty(ApiKey)
            ? $"{BaseUrl}/fapi/v1/leverageBracket?{query}"
            : SignedUrl("/fapi/v1/leverageBracket", query);

        Log($"GET /fapi/v1/leverageBracket {symbol}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Add("X-MBX-APIKEY", ApiKey);
            }

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"GetLeverageBrackets error: {content}");
                return [];
            }

            var rows = JsonSerializer.Deserialize<List<LeverageBracketResponse>>(content) ?? [];
            return rows.FirstOrDefault(r =>
                r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))?.Brackets ?? [];
        }
        catch (Exception ex)
        {
            Log($"GetLeverageBrackets: {ex.Message}");
            return [];
        }
    }

    public async Task<bool> SetLeverageAsync(string symbol, int leverage)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol))
        {
            return false;
        }

        var url = SignedUrl("/fapi/v1/leverage", $"symbol={symbol.ToUpperInvariant()}&leverage={leverage}&timestamp={{0}}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/leverage");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<BinanceOrder> PlaceOrderAsync(
        string symbol,
        string side,
        string type,
        string quantity,
        string? price = null,
        bool reduceOnly = false,
        string timeInForce = "GTC")
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
        if (reduceOnly)
        {
            sb.Append("&reduceOnly=true");
        }

        if (type.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Цена обязательна для LIMIT");
            }

            sb.Append($"&price={price}");
            sb.Append($"&timeInForce={timeInForce.ToUpperInvariant()}");
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

    public async Task<BinanceOrder> PlaceStopOrderAsync(
        string symbol,
        string side,
        string type,
        string quantity,
        string stopPrice,
        string? price = null,
        string workingType = "CONTRACT_PRICE",
        string timeInForce = "GTC",
        bool reduceOnly = false)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            throw new InvalidOperationException("API credentials are not set.");
        }

        var normalizedType = type.ToUpperInvariant();
        if (normalizedType is "STOP_MARKET" or "STOP" or "TAKE_PROFIT_MARKET" or "TAKE_PROFIT")
        {
            return await PlaceAlgoConditionalOrderAsync(
                symbol,
                side,
                normalizedType,
                quantity,
                stopPrice,
                price,
                workingType,
                timeInForce,
                reduceOnly).ConfigureAwait(false);
        }

        return await PlaceLegacyStopOrderAsync(
            symbol,
            side,
            normalizedType,
            quantity,
            stopPrice,
            price,
            workingType,
            timeInForce,
            reduceOnly).ConfigureAwait(false);
    }

    private async Task<BinanceOrder> PlaceAlgoConditionalOrderAsync(
        string symbol,
        string side,
        string type,
        string quantity,
        string triggerPrice,
        string? price,
        string workingType,
        string timeInForce,
        bool reduceOnly)
    {
        var sb = new StringBuilder();
        sb.Append("algoType=CONDITIONAL");
        sb.Append($"&symbol={symbol.ToUpperInvariant()}");
        sb.Append($"&side={side.ToUpperInvariant()}");
        sb.Append($"&type={type}");
        sb.Append($"&quantity={quantity}");
        sb.Append($"&triggerPrice={triggerPrice}");
        sb.Append($"&workingType={workingType.ToUpperInvariant()}");

        if (type is "STOP" or "TAKE_PROFIT")
        {
            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Цена обязательна для STOP/TAKE_PROFIT limit");
            }

            sb.Append($"&price={price}");
            sb.Append($"&timeInForce={timeInForce.ToUpperInvariant()}");
        }

        if (reduceOnly)
        {
            sb.Append("&reduceOnly=true");
        }

        var url = SignedUrl("/fapi/v1/algoOrder", sb.ToString() + "&timestamp={0}", usePost: true);
        Log($"POST /fapi/v1/algoOrder {type} {side} {symbol} trigger={triggerPrice}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/algoOrder");
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"AlgoOrder error: {content}");
            throw new Exception($"API Error: {content}");
        }

        var algo = JsonSerializer.Deserialize<BinanceAlgoOrderResponse>(content)
                   ?? throw new Exception("Empty algo order response");
        return new BinanceOrder
        {
            OrderId = algo.AlgoId,
            Symbol = algo.Symbol,
            Side = algo.Side,
            Type = algo.OrderType,
            OrigQty = algo.Quantity,
            Status = algo.AlgoStatus,
            StopPrice = algo.TriggerPrice,
            Price = algo.Price,
        };
    }

    private async Task<BinanceOrder> PlaceLegacyStopOrderAsync(
        string symbol,
        string side,
        string normalizedType,
        string quantity,
        string stopPrice,
        string? price,
        string workingType,
        string timeInForce,
        bool reduceOnly)
    {
        var sb = new StringBuilder();
        sb.Append($"symbol={symbol.ToUpperInvariant()}");
        sb.Append($"&side={side.ToUpperInvariant()}");
        sb.Append($"&type={normalizedType}");
        sb.Append($"&quantity={quantity}");
        sb.Append($"&stopPrice={stopPrice}");
        sb.Append($"&workingType={workingType.ToUpperInvariant()}");

        if (normalizedType is "STOP" or "TAKE_PROFIT")
        {
            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Цена обязательна для STOP/TAKE_PROFIT limit");
            }

            sb.Append($"&price={price}");
            sb.Append($"&timeInForce={timeInForce.ToUpperInvariant()}");
        }

        if (reduceOnly)
        {
            sb.Append("&reduceOnly=true");
        }

        var url = SignedUrl("/fapi/v1/order", sb.ToString() + "&timestamp={0}", usePost: true);
        Log($"POST /fapi/v1/order {normalizedType} {side} {symbol} stop={stopPrice}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fapi/v1/order");
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        request.Content = new StringContent(ExtractBody(url), Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"StopOrder error: {content}");
            throw new Exception($"API Error: {content}");
        }

        return JsonSerializer.Deserialize<BinanceOrder>(content)
               ?? throw new Exception("Empty stop order response");
    }

    public Task<BinanceOrder> PlaceConditionalOrderAsync(
        string symbol,
        string side,
        string type,
        string quantity,
        string stopPrice) =>
        PlaceStopOrderAsync(symbol, side, type, quantity, stopPrice, reduceOnly: true);

    public async Task CancelConditionalOrdersAsync(string symbol, Func<string, bool> typeFilter)
    {
        var openOrders = await GetOpenOrdersAsync(symbol).ConfigureAwait(false);
        foreach (var order in openOrders.Where(o => typeFilter(o.Type)))
        {
            await CancelOrderAsync(symbol, order.OrderId).ConfigureAwait(false);
        }

        var algoOrders = await GetOpenAlgoOrdersAsync(symbol).ConfigureAwait(false);
        foreach (var algo in algoOrders.Where(o => typeFilter(o.OrderType)))
        {
            await CancelAlgoOrderAsync(symbol, algo.AlgoId).ConfigureAwait(false);
        }
    }

    public async Task<List<BinanceAlgoOrderResponse>> GetOpenAlgoOrdersAsync(string? symbol = null)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return [];
        }

        var query = string.IsNullOrEmpty(symbol)
            ? "timestamp={0}"
            : $"symbol={symbol.ToUpperInvariant()}&timestamp={{0}}";
        var url = SignedUrl("/fapi/v1/openAlgoOrders", query);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"OpenAlgoOrders error: {content}");
            return [];
        }

        return JsonSerializer.Deserialize<List<BinanceAlgoOrderResponse>>(content) ?? [];
    }

    public async Task<bool> CancelAlgoOrderAsync(string symbol, long algoId)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return false;
        }

        var query = $"symbol={symbol.ToUpperInvariant()}&algoId={algoId}&timestamp={{0}}";
        var url = SignedUrl("/fapi/v1/algoOrder", query);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"CancelAlgoOrder error: {content}");
            return false;
        }

        return true;
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

    public async Task<List<UserTradeResponse>> GetUserTradesSinceAsync(
        string symbol,
        long startTimeMs,
        int pageSize = 1000,
        CancellationToken ct = default)
    {
        var all = new List<UserTradeResponse>();
        long? fromId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await GetUserTradesAsync(symbol, startTimeMs, fromId: fromId, limit: pageSize, ct: ct)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            all.AddRange(batch);
            if (batch.Count < pageSize)
            {
                break;
            }

            fromId = batch.Max(t => t.Id) + 1;
        }

        return all;
    }

    public async Task<List<UserTradeResponse>> GetUserTradesAsync(
        string symbol,
        long? startTime = null,
        long? endTime = null,
        long? fromId = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey) || string.IsNullOrEmpty(symbol))
        {
            return [];
        }

        var sb = new StringBuilder();
        sb.Append($"symbol={symbol.ToUpperInvariant()}");
        sb.Append(CultureInfo.InvariantCulture, $"&limit={Math.Clamp(limit, 1, 1000)}");
        if (startTime.HasValue)
        {
            sb.Append(CultureInfo.InvariantCulture, $"&startTime={startTime.Value}");
        }

        if (endTime.HasValue)
        {
            sb.Append(CultureInfo.InvariantCulture, $"&endTime={endTime.Value}");
        }

        if (fromId.HasValue)
        {
            sb.Append(CultureInfo.InvariantCulture, $"&fromId={fromId.Value}");
        }

        sb.Append("&timestamp={0}");
        var url = SignedUrl("/fapi/v1/userTrades", sb.ToString());
        Log($"GET /fapi/v1/userTrades {symbol}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"UserTrades error: {content}");
            return [];
        }

        return JsonSerializer.Deserialize<List<UserTradeResponse>>(content) ?? [];
    }

    public async Task<List<FuturesIncomeRecord>> GetIncomeSinceAsync(
        long startTimeMs,
        string? incomeType = null,
        CancellationToken ct = default)
    {
        var all = new List<FuturesIncomeRecord>();
        for (var page = 1; page <= 100; page++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await GetIncomePageAsync(startTimeMs, incomeType, page, ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            all.AddRange(batch);
            Log($"GET /fapi/v1/income page={page} rows={batch.Count} total={all.Count}");
            if (batch.Count < 1000)
            {
                break;
            }
        }

        return all;
    }

    private async Task<List<FuturesIncomeRecord>> GetIncomePageAsync(
        long startTimeMs,
        string? incomeType,
        int page,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(SecretKey))
        {
            return [];
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"startTime={startTimeMs}");
        sb.Append(CultureInfo.InvariantCulture, $"&limit=1000&page={page}");
        if (!string.IsNullOrWhiteSpace(incomeType))
        {
            sb.Append($"&incomeType={incomeType}");
        }

        sb.Append("&timestamp={0}");
        var url = SignedUrl("/fapi/v1/income", sb.ToString());
        Log($"GET /fapi/v1/income page={page} type={incomeType ?? "ALL"}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", ApiKey);
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"Income error: {content}");
            return [];
        }

        return JsonSerializer.Deserialize<List<FuturesIncomeRecord>>(content) ?? [];
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

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
