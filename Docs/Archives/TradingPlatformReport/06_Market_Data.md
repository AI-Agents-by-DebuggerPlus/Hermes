# 06. Рыночные данные

## 6.1. Контракт `IMarketDataFeed`

`Hermes.TradingPlatform.Core/Abstractions/IMarketDataFeed.cs`:

```5:11:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Abstractions/IMarketDataFeed.cs
public interface IMarketDataFeed : IDisposable
{
    string Name { get; }
    MarketFeedStatus Status { get; }
    event EventHandler? StatusChanged;
    void Start();
}
```

Простой контракт: создал → `Start()` → фид сам публикует события в `IEventBus`. Останавливается через `Dispose`. Статус → 5 значений (`Stopped`, `Connecting`, `Connected`, `Reconnecting`, `Error`).

Реализаций две: `BinanceFuturesMarketDataFeed` (живой WebSocket) и `MockMarketDataFeed` (random walk). Источник выбирается в Settings UI (`MarketDataSource` enum: `Mock` / `BinanceFutures`).

`TradingPlatformHost.RestartMarketFeed` создаёт нужный экземпляр и подключает обработчик `StatusChanged → FeedStatusChanged` для UI.

## 6.2. `BinanceFuturesMarketDataFeed`

Файл: `Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs`.

### 6.2.1. Endpoints

```15:17:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs
    private const string SingleStreamBase = "wss://fstream.binance.com/ws";
    private const string RestBase = "https://fapi.binance.com";
    private static readonly TimeSpan TwentyFourHrPollInterval = TimeSpan.FromMinutes(1);
```

- **WebSocket**: `wss://fstream.binance.com/ws` — публичный поток USDT-M Futures.
- **REST**: `https://fapi.binance.com/fapi/v1/ticker/24hr?symbol=...` — для 24-hr статистики (опрос каждые 60 с).
- Это **futures**, не spot. Реальные котировки фьючерсов BTCUSDT-PERPETUAL etc.

### 6.2.2. Подписки

Каждый символ генерирует 4 потока (см. `SendSubscribeAsync`):

```302:323:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs
        // Streams per symbol:
        //   bookTicker  – per-update best bid/ask (lowest-latency price source)
        //   ticker      – 1s rolling 24h stats (price + change% + 24h volume)
        //   aggTrade    – aggregated trades (real fills, drives MarketTradeEvent for tape)
        //   kline_1m    – 1-minute candles (drives MarketKlineEvent for charts/Replay)
```

JSON-сообщение `{"method":"SUBSCRIBE","params":[...],"id":1}` отправляется сразу после connect.

### 6.2.3. Параллельные задачи

- `_runTask` — главный WebSocket loop (`RunAsync` → `ReceiveLoopAsync`).
- `_statsTask` — fallback poller REST `/24hr` каждые 60 с (`Poll24hrStatsLoopAsync`).

### 6.2.4. WebSocket reconnect

```127:191:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                SetStatus(MarketFeedStatus.Connecting);
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(new Uri(SingleStreamBase), cancellationToken);
                SetStatus(MarketFeedStatus.Connected);
                PublishLog($"Connected to Binance Futures ({_symbols.Count} symbols)");

                await SendSubscribeAsync(socket, cancellationToken);
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SetStatus(MarketFeedStatus.Error);
                PublishLog($"Binance feed error: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); }
                catch (OperationCanceledException) { break; }
                SetStatus(MarketFeedStatus.Reconnecting);
            }
            finally
            {
                if (socket is not null) { ... socket.Dispose(); }
            }
        }
        SetStatus(MarketFeedStatus.Stopped);
    }
```

- **Resilient**: при любой ошибке (DNS, dropped connection, server close) — задержка 5 секунд + переход в `Reconnecting` → новый цикл.
- KeepAlive 30 сек.
- На каждой ошибке — `PlatformLogEvent` EventType=`"Market"`, Source=`"BinanceFutures"`.
- Первый кадр после reconnect логируется с превью (`"first frame received (N bytes)..."`).

### 6.2.5. Обработка сообщений

`ReceiveLoopAsync` буферизует фреймы (до `EndOfMessage`), парсит через `BinanceFuturesStreamParser.TryParseMessage` и публикует:

| Binance тип | Парсер → message | Публикация |
|---|---|---|
| `24hrTicker` (1s rolling) | `BinanceTickerStreamMessage(symbol, lastPrice, changePct, qVolume)` | `MarketTickEvent(symbol, price, bid=price-spread, ask=price+spread, change?, volume?)` где `spread = price * 0.0001` |
| `bookTicker` | `BinanceTickerStreamMessage(symbol, mid, 0, 0)` | (через тот же парсер `BinanceTickerStreamMessage`) `MarketTickEvent` |
| `aggTrade` | `BinanceAggTradeStreamMessage(symbol, price, qty, aggressorBuy, tradeId, tradeTime)` | **И** `MarketTradeEvent` **И** `MarketTickEvent` (synthetic tick, чтобы стратегии видели sub-second движения) |
| `kline` (1m candles) | `BinanceKlineStreamMessage(...)` | `MarketKlineEvent` |

Это значит, что **`MarketTickEvent` публикуется и от `@ticker`, и от `@aggTrade`, и от `@bookTicker`**. На активных символах это **много фиков в секунду**. Каждый из них:
- триггерит `MarketTickProjection` (state mutation, equity recalc),
- триггерит `VirtualExchangeEngine.OnMarketTick` (попытка fill открытых ордеров),
- триггерит `StrategyRunner.OnMarketTick` (cooldown-фильтр часто блокирует),
- триггерит `HermesOrchestrationService.OnMarketTick` (20-секундный throttle),
- триггерит `RiskCircuitBreaker.Evaluate` (cheap pre-check),
- триггерит `MainViewModel.RefreshAccountSummary` через `StateChanged`.

### 6.2.6. REST `/24hr` poller

`Poll24hrStatsLoopAsync` каждые 60 с обходит все символы и публикует `MarketTickEvent` с `change` и `volume` из `/fapi/v1/ticker/24hr`. Это дублирует поток `@ticker`, но **полезно в момент connect**: пока WS подключается, REST уже даёт первый тик, и `state.Tickers.Price` обновляется быстрее.

```93:125:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/MarketData/BinanceFuturesMarketDataFeed.cs
    private async Task PublishOne24hrStatAsync(string symbol, CancellationToken cancellationToken)
    {
        var url = $"{RestBase}/fapi/v1/ticker/24hr?symbol={symbol}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var lastPriceStr = root.GetProperty("lastPrice").GetString();
        // ... parse decimals ...

        var spread = price * 0.0001m;
        _bus.Publish(new MarketTickEvent(symbol, price, price - spread, price + spread, change, volume));
    }
```

Если HTTP failed → silently skip (не публикует тик).

### 6.2.7. Диагностические логи

Конструктор принимает `Action<string>? diagnosticLog`, который пишет в `TradingPlatformFileLogger`. Логируются:
- Старт фида + список символов.
- Connect / disconnect / error / reconnect.
- Первый кадр (preview 200 байт).
- Heartbeat раз в 60 секунд (`framesSeen=N`).
- Первый тик каждого символа.
- Subscribe-payload.

Файл логов: `D:/Programming/AI_Agents/Hermes/Logs/Hermes.TradingPlatform/trading_session_<stamp>.log`.

## 6.3. `MockMarketDataFeed`

Файл: `Hermes.TradingPlatform.Exchange/MarketData/MockMarketDataFeed.cs`.

```7:46:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/MarketData/MockMarketDataFeed.cs
public sealed class MockMarketDataFeed : IMarketDataFeed
{
    private readonly IEventBus _bus;
    private readonly Dictionary<string, decimal> _prices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new(42);
    private Timer? _timer;

    public MockMarketDataFeed(IEventBus bus, IEnumerable<(string Symbol, decimal Price)> seedPrices) { ... }

    public void Start(TimeSpan? interval = null)
    {
        _timer?.Dispose();
        SetStatus(MarketFeedStatus.Connected);
        _timer = new Timer(_ => PublishTicks(), null, TimeSpan.Zero, interval ?? TimeSpan.FromSeconds(2));
    }

    private void PublishTicks()
    {
        foreach (var (symbol, price) in _prices.ToList())
        {
            var delta = (decimal)(_random.NextDouble() - 0.5) * price * 0.0008m;
            var next = Math.Max(0.01m, price + delta);
            _prices[symbol] = next;
            var spread = next * 0.0001m;
            _bus.Publish(new MarketTickEvent(symbol, next, next - spread, next + spread));
        }
    }
}
```

- **Random walk** с амплитудой ±0.04% от цены каждые 2 секунды.
- Seed `Random(42)` — детерминированно для каждой сессии, но между сессиями старт всегда одинаков.
- Стартовые цены из `state.Tickers` (создано `InitialTradingSeed`).
- `ChangePercent24h` и `QuoteVolume24h` не публикуются (стратегии Mock-режим **не получат сигналов на 24h-условиях**).
- Статус сразу `Connected`. Никаких error/reconnect путей.

## 6.4. Переключение источника в рантайме

`TradingPlatformHost.SetMarketDataSource(MarketDataSource source)` (используется из `SettingsViewModel.ApplyMarketDataMode`):

```322:333:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Services/TradingPlatformHost.cs
    public void SetMarketDataSource(MarketDataSource source)
    {
        MarketDataSource = source;
        var current = PlatformSettingsStore.Load();
        PlatformSettingsStore.Save(CopySettings(current, s => s.MarketDataSource = PlatformSettingsFileStore.ToStorageValue(source)));
        RestartMarketFeed();
    }
```

`RestartMarketFeed`:
1. Останавливает старый фид (`Dispose` → cancellation token).
2. Берёт текущий список символов из `state.Tickers`.
3. Конструирует новый `BinanceFuturesMarketDataFeed` или `MockMarketDataFeed`.
4. `Start()`.
5. Логирует системное сообщение "Market data: BinanceFutures (Binance Futures (live))".

UI-метка `FeedStatusLabel` (см. `TradingPlatformHost.FeedStatusLabel`):

```214:228:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Services/TradingPlatformHost.cs
    public string FeedStatusLabel => _activeFeed?.Status switch
    {
        MarketFeedStatus.Connected => MarketDataSource == MarketDataSource.BinanceFutures ? "BINANCE LIVE" : "SIMULATION",
        MarketFeedStatus.Connecting => "CONNECTING…",
        MarketFeedStatus.Reconnecting => "RECONNECTING…",
        MarketFeedStatus.Error => "FEED ERROR",
        _ => "STOPPED",
    };
```

Отображается в топ-баре `MainViewModel.ConnectionStatus`.

## 6.5. Сохранение настройки

`PlatformSettingsFileStore` хранит выбор в `platform-settings.json` (`MarketDataSource` поле). При старте `MigrateMarketDataToBinanceFutures` принудительно меняет legacy "Mock" на "BinanceFutures" — это значит, что после обновления версии пользователь **по умолчанию** попадёт на Binance live, даже если раньше использовал Mock.

```72:80:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/PlatformSettingsFileStore.cs
    private static PlatformSettingsDto MigrateMarketDataToBinanceFutures(PlatformSettingsDto dto)
    {
        if (string.Equals(dto.MarketDataSource, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            dto.MarketDataSource = "BinanceFutures";
        }
        return dto;
    }
```

## 6.6. Какие символы публикуются

Список берётся из `state.Tickers.Symbol`. Стандартный seed (`InitialTradingSeed.Create`):

- BTCUSDT, ETHUSDT, SOLUSDT — `InWatchlist=true`.
- BNBUSDT — `InWatchlist=false`.

При сбросе аккаунта (`CreateClean`) остаются только первые 3. Из UI добавить символ **нельзя** (нет команды) — символы зафиксированы в seed.

## 6.7. Quote source: spot vs futures

В предыдущих чатах было явно подтверждено: **`fstream.binance.com` → USDT-M Perpetual Futures**, не spot (`stream.binance.com`). Это значит, что котировки могут отличаться от спотовых на величину basis. Все стратегии и UI ведут себя как фьючерсный терминал (отсюда поддержка plein/short, leverage).

## 6.8. Резервные и не-используемые конструкции

- `BinanceFuturesStreamParser.TryParseTickerMessage` (extract `Update` без обёртки) — экспортирован, но **не вызывается** из текущего кода. Это, видимо, артефакт ранней архитектуры.
- `MarketTradeEvent` и `MarketKlineEvent` публикуются Binance-фидом, но **подписчиков на них нет** в текущем коде. Это значит, что aggTrade tape и klines собираются впустую (хотя расход — только bus.Publish, проекций нет).
- `BookTicker` сообщения парсятся, но **не используются для bid/ask напрямую** — публикуются как `MarketTickEvent(symbol, mid, mid-spread, mid+spread)`, где spread берётся не из bid/ask, а из формулы `mid * 0.0001`. То есть **bid/ask из MarketTickEvent — это синтетика, не реальный спред**.
