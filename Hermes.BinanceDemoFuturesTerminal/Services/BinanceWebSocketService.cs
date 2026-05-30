using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes.BinanceDemoFuturesTerminal.Services
{
    public class BinanceWebSocketService
    {
        private const string WsUrl = "wss://demo-fstream.binance.com/ws";
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly Action<string> _logger;
        private readonly List<string> _activeStreams = new List<string>();
        private string _currentSymbol;

        public event Action<WsTickerPayload> OnTickerReceived;
        public event Action<WsDepthPayload> OnDepthReceived;
        public event Action<WsTradePayload> OnTradeReceived;
        public event Action<WsKlinePayload> OnKlineReceived;
        public event Action<string> OnConnectionStatusChanged;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public BinanceWebSocketService(Action<string> logger = null)
        {
            _logger = logger;
        }

        private void Log(string message)
        {
            _logger?.Invoke($"[WS] {DateTime.Now:HH:mm:ss.fff} | {message}");
        }

        // Подключение к WebSocket
        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            Log("Подключение к WebSocket Demo...");
            OnConnectionStatusChanged?.Invoke("Подключение...");

            try
            {
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                await _ws.ConnectAsync(new Uri(WsUrl), _cts.Token);
                Log("Успешно подключено к WebSocket Demo.");
                OnConnectionStatusChanged?.Invoke("Подключено");

                // Запуск потока получения сообщений
                _ = Task.Run(ReceiveLoop, _cts.Token);
            }
            catch (Exception ex)
            {
                Log($"Ошибка подключения к WebSocket: {ex.Message}");
                OnConnectionStatusChanged?.Invoke("Отключено");
            }
        }

        // Подписка на стримы конкретной торговой пары
        public async Task SubscribeSymbolAsync(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            symbol = symbol.ToLower();

            await ConnectAsync();

            if (!IsConnected)
            {
                Log("Ошибка подписки: WebSocket не подключен.");
                return;
            }

            // Отписка от предыдущей пары, если она была выбрана
            if (!string.IsNullOrEmpty(_currentSymbol) && _currentSymbol != symbol)
            {
                await UnsubscribeSymbolAsync(_currentSymbol);
            }

            _currentSymbol = symbol;

            var streams = new List<string>
            {
                $"{symbol}@ticker",
                $"{symbol}@depth20@100ms",
                $"{symbol}@trade",
                $"{symbol}@kline_1m"
            };

            var request = new
            {
                method = "SUBSCRIBE",
                @params = streams,
                id = DateTime.Now.Ticks
            };

            string json = JsonSerializer.Serialize(request);
            Log($"Отправка запроса подписки для {symbol.ToUpper()}");
            await SendMessageAsync(json);

            lock (_activeStreams)
            {
                _activeStreams.AddRange(streams);
            }
        }

        // Отписка от стримов торговой пары
        public async Task UnsubscribeSymbolAsync(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || !IsConnected) return;
            symbol = symbol.ToLower();

            var streams = new List<string>
            {
                $"{symbol}@ticker",
                $"{symbol}@depth20@100ms",
                $"{symbol}@trade",
                $"{symbol}@kline_1m"
            };

            var request = new
            {
                method = "UNSUBSCRIBE",
                @params = streams,
                id = DateTime.Now.Ticks
            };

            string json = JsonSerializer.Serialize(request);
            Log($"Отправка запроса отписки от {symbol.ToUpper()}");
            await SendMessageAsync(json);

            lock (_activeStreams)
            {
                foreach (var s in streams)
                {
                    _activeStreams.Remove(s);
                }
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (!IsConnected) return;
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // Фоновый цикл прослушивания сокета
        private async Task ReceiveLoop()
        {
            var buffer = new byte[65536]; // Буфер 64 КБ для больших JSON-ответов стакана
            var ms = new System.IO.MemoryStream();

            try
            {
                while (IsConnected && !_cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    ms.SetLength(0);

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log("Сессия WebSocket закрыта сервером.");
                        break;
                    }

                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessMessage(json);
                }
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    Log($"Исключение чтения WebSocket: {ex.Message}");
                }
            }
            finally
            {
                await DisconnectInternalAsync();
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out _) && root.TryGetProperty("id", out _))
                {
                    return;
                }

                if (root.TryGetProperty("data", out var dataEl))
                {
                    ProcessPayload(dataEl.GetRawText());
                    return;
                }

                ProcessPayload(json);
            }
            catch
            {
                // ignore parse errors
            }
        }

        private void ProcessPayload(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("e", out var eventProp))
            {
                var eventType = eventProp.GetString();
                if (eventType == "24hrTicker")
                {
                    var ticker = JsonSerializer.Deserialize<WsTickerPayload>(json);
                    if (ticker != null) OnTickerReceived?.Invoke(ticker);
                    return;
                }

                if (eventType == "depthUpdate")
                {
                    var depth = JsonSerializer.Deserialize<WsDepthPayload>(json);
                    if (depth != null) OnDepthReceived?.Invoke(depth);
                    return;
                }

                if (eventType == "trade" || eventType == "aggTrade")
                {
                    var trade = JsonSerializer.Deserialize<WsTradePayload>(json);
                    if (trade != null) OnTradeReceived?.Invoke(trade);
                    return;
                }

                if (eventType == "kline")
                {
                    var kline = JsonSerializer.Deserialize<WsKlinePayload>(json);
                    if (kline != null) OnKlineReceived?.Invoke(kline);
                    return;
                }
            }

            if (root.TryGetProperty("bids", out _) && root.TryGetProperty("asks", out _))
            {
                var depth = JsonSerializer.Deserialize<WsDepthPayload>(json);
                if (depth != null) OnDepthReceived?.Invoke(depth);
            }
        }

        public async Task DisconnectAsync()
        {
            Log("Отключение от WebSocket по запросу пользователя...");
            await DisconnectInternalAsync();
        }

        private async Task DisconnectInternalAsync()
        {
            try
            {
                _cts?.Cancel();
                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    _ws.Dispose();
                    _ws = null;
                }
            }
            catch { }

            Log("Соединение WebSocket закрыто.");
            OnConnectionStatusChanged?.Invoke("Отключено");
        }
    }

    #region Пайлоады WebSocket
    public class WsTickerPayload
    {
        [JsonPropertyName("s")]
        public string Symbol { get; set; }

        [JsonPropertyName("c")]
        public string LastPrice { get; set; }

        [JsonPropertyName("P")]
        public string PriceChangePercent { get; set; }

        [JsonPropertyName("h")]
        public string HighPrice { get; set; }

        [JsonPropertyName("l")]
        public string LowPrice { get; set; }

        [JsonPropertyName("v")]
        public string Volume { get; set; }
    }

    public class WsDepthPayload
    {
        [JsonPropertyName("lastUpdateId")]
        public long LastUpdateId { get; set; }

        [JsonPropertyName("bids")]
        public List<List<string>> Bids { get; set; }

        [JsonPropertyName("asks")]
        public List<List<string>> Asks { get; set; }

        /// <summary>Futures depthUpdate bids.</summary>
        [JsonPropertyName("b")]
        public List<List<string>> B { get; set; }

        /// <summary>Futures depthUpdate asks.</summary>
        [JsonPropertyName("a")]
        public List<List<string>> A { get; set; }

        public IReadOnlyList<List<string>> GetBids() => Bids ?? B ?? [];

        public IReadOnlyList<List<string>> GetAsks() => Asks ?? A ?? [];
    }

    public class WsTradePayload
    {
        [JsonPropertyName("s")]
        public string Symbol { get; set; }

        [JsonPropertyName("p")]
        public string Price { get; set; }

        [JsonPropertyName("q")]
        public string Qty { get; set; }

        [JsonPropertyName("T")]
        public long Time { get; set; }

        [JsonPropertyName("m")]
        public bool IsBuyerMaker { get; set; } // true = продажа по маркету, false = покупка
    }

    public class WsKlinePayload
    {
        [JsonPropertyName("s")]
        public string Symbol { get; set; }

        [JsonPropertyName("k")]
        public WsKlineData KlineData { get; set; }
    }

    public class WsKlineData
    {
        [JsonPropertyName("t")]
        public long OpenTime { get; set; }

        [JsonPropertyName("T")]
        public long CloseTime { get; set; }

        [JsonPropertyName("o")]
        public string Open { get; set; }

        [JsonPropertyName("c")]
        public string Close { get; set; }

        [JsonPropertyName("h")]
        public string High { get; set; }

        [JsonPropertyName("l")]
        public string Low { get; set; }

        [JsonPropertyName("v")]
        public string Volume { get; set; }

        [JsonPropertyName("x")]
        public bool IsClosed { get; set; }
    }
    #endregion
}
