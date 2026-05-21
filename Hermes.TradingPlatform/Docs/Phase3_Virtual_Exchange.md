# Phase 3 — Virtual exchange (реализовано, MVP)

## Exchange (`Hermes.TradingPlatform.Exchange`)

### `VirtualExchangeEngine`
- **Market** — немедленное исполнение со slippage (~0.02%)
- **Limit** — fill on touch при тике
- **Stop** — упрощённый trigger по `TriggerPrice` / `Price`
- **Fees** — taker 0.04%
- **Cancel** — `TryCancelOrder` → `OrderCancelledEvent`

### `MockMarketDataFeed`
- Random walk каждые ~2 с по символам из seed
- Публикует `MarketTickEvent` → проекции + проверка лимитов

## Risk (`Hermes.TradingPlatform.Risk`)
- `RiskValidator` — emergency halt, safe mode (только reduce-only), max BTC size, leverage
- UI **Emergency Stop** → `RiskTriggeredEvent` + halt strategies

## Поведение в UI
- **Orders → Cancel** — реально отменяет open-ордер в state
- **Orders → «Новый ордер»** — форма Place order (Market/Limit/Stop, RO)
- **Risk Manager** — лимиты пишутся в `ITradingStateStore` + `%LocalAppData%\HermesTrading\risk-profile.json`
- **Logs** — пополняется при тиках, ордерах, risk
- **Market Watch / Positions / Dashboard** — обновляются на тиках
- **Modify** — пока заглушка

## Не входит в Phase 3
- Matching engine / order book
- Binance WebSocket (Phase 4)
- Strategy execution (Phase 5)
- Hermes orchestration (Phase 6)
