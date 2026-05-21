# Phase 4 — Binance market data (реализовано)

## Exchange (`Hermes.TradingPlatform.Exchange.MarketData`)

### `BinanceFuturesMarketDataFeed`
- Публичный WebSocket USDT-M Futures: `wss://fstream.binance.com/stream?streams=…`
- Стрим `@ticker` (24hr) — last price, % change 24h, quote volume
- Публикует `MarketTickEvent` в event bus
- Авто-reconnect при обрыве

### `MockMarketDataFeed`
- Random walk (как раньше), режим по умолчанию / fallback

### `IMarketDataFeed`
- Единый контракт: `Start()`, `Status`, `StatusChanged`

## Настройки

- `%LocalAppData%\HermesTrading\platform-settings.json`
- `MarketDataSource`: `Mock` | `BinanceFutures`
- **Settings → Market data → Apply** перезапускает feed

## UI

- Top bar: `BINANCE LIVE` / `SIMULATION` / `CONNECTING…` / `FEED ERROR`
- Market Watch: живые цены и 24h % с Binance (при live-режиме)
- Logs: подключение / ошибки `BinanceFutures`

## Не входит в Phase 4

- Приватные ключи / торговля на Binance (только публичные маркет-данные)
- Свечи, trades, order book depth (можно добавить позже)
- Исполнение ордеров на реальной бирже
