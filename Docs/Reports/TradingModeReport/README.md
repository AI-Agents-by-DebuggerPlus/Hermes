# Hermes.Wpf — режим трейдинга: отчёт о логике работы

**Дата отчёта:** 2026-05-30  
**Основные проекты:** `Hermes.Wpf`, `Hermes.BinanceDemoFuturesTerminal`, `Hermes.Terminals.Shared`  
**Связанные документы:** [BinanceDemoFutures Agent Testing](../../Instructions/BinanceDemoFutures_Agent_Testing.ru.md), [Review Backlog](../../Plans/BinanceDemoFutures_Review_Backlog.ru.md)

---

## Назначение отчёта

Документ описывает **логику режима трейдинга** в Hermes.Wpf: как пользователь включает режим, как формируется контекст для LLM, как команды доходят до Binance Demo Futures Terminal и где срабатывают уровни риск-контроля.

Отчёт не дублирует исходный код — он навигационный: каждый раздел указывает файлы, классы и потоки данных.

---

## Кратко: что такое «режим трейдинга»

Режим трейдинга — это персона **трейдер-исполнитель** в чате Hermes.Wpf, связанная с **USDT-M Futures Demo** (`demo-fapi.binance.com`) через файловый bridge.

Пользователь пишет простым языком («открой лонг по биткоину по рынку»). Hermes.Wpf либо:

1. **Локально** распознаёт типовые фразы и отправляет ордер без вызова LLM, либо  
2. **Через агента** (Hermes CLI / WSL): дополняет промпт snapshot терминала, получает JSON `skill:trading`, исполняет команду через bridge.

**Жёсткая защита на бирже** — риск-менеджер в терминале (`RiskManager`).  
**Дополнительная защита** — текстовые правила пользователя в промпте агента (могут быть **строже** настроек терминала).

---

## Структура отчёта

| Файл | Содержание |
|------|------------|
| [01_Overview.md](./01_Overview.md) | Архитектура, компоненты, схема потоков |
| [02_Mode_Switching.md](./02_Mode_Switching.md) | Включение/выключение, триггеры, gate, роли, UI |
| [03_Prompt_And_Context.md](./03_Prompt_And_Context.md) | Сборка промпта, persona, snapshot, правила безопасности |
| [04_Command_Execution.md](./04_Command_Execution.md) | JSON → parser → executor → bridge → терминал → API |
| [05_Local_Handlers.md](./05_Local_Handlers.md) | Локальные парсеры без LLM (open, close, status) |
| [06_Risk_Layers.md](./06_Risk_Layers.md) | Три уровня риска: агент, Hermes.Wpf, терминал |
| [07_Bridge_And_Snapshot.md](./07_Bridge_And_Snapshot.md) | Файлы bridge, heartbeat, поля snapshot |
| [08_Settings_And_Config.md](./08_Settings_And_Config.md) | HermesSettings, PlatformSettings, UI настроек |
| [09_Diagnostics.md](./09_Diagnostics.md) | Пути, логи, типичные сбои |

---

## TL;DR — цепочка одного ордера

```
Пользователь → MainViewModel.ExecuteHermesUserTurnAsync
  → [локальный парсер?] → FuturesTradingCommandExecutor
  → [иначе] BuildOutboundHermesPrompt → Hermes CLI
  → TradingPlatformIntentParser (JSON skill:trading)
  → FuturesTerminalBridgeService.EnqueueCommandAsync
  → %LocalAppData%/HermesFutures/bridge/commands.json
  → FuturesBridgeCommandProcessor (терминал)
  → MainViewModel.ExecuteBridgeCommandAsync
  → RiskManager.ValidateOrder → BinanceApiService
  → snapshot.json обновляется → следующий промпт агента
```

---

## Связь с другими «торговыми» компонентами репозитория

| Компонент | Роль в режиме трейдинга |
|-----------|-------------------------|
| **Hermes.BinanceDemoFuturesTerminal** | Основной исполнитель (Demo Futures) |
| **Hermes.SpotTerminal** | Опционально, если включена spot-интеграция |
| **Hermes.TradingPlatform** | Отдельная paper-платформа; включается через `TradingPlatformIntegrationEnabled`, не путать с Demo Futures |
