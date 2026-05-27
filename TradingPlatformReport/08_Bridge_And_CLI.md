# 08. Мост (Bridge) и CLI

Trading Platform — отдельный WPF-процесс (`Hermes.TradingPlatform.Wpf.exe`). С внешним миром (`Hermes.Wpf` и `Hermes.TradingPlatform.Cli`) он общается через **файловый IPC** в `%LocalAppData%/HermesTrading/bridge/`:

| Файл | Направление | Назначение |
|---|---|---|
| `snapshot.json` | terminal → consumer | Текущее состояние (см. `TradingPlatformSnapshotFile`). |
| `heartbeat.txt` | terminal → consumer | ISO-8601 UTC, обновляется каждые 3 с. Терминал жив, если delta < 12 с. |
| `commands.json` | consumer → terminal | Очередь команд (action JSON). |
| `result-<guid>.json` | terminal → consumer | Результат одной команды (для CLI `wait-result`). |

> Конструктор: **никаких сокетов, gRPC или named pipes**. Это сознательно — Hermes.Wpf и Hermes.TradingPlatform остаются независимыми WPF-процессами; пользователь может запустить только один из них.

## 8.1. Публикация: `TradingBridgePublisher`

Файл: `Hermes.TradingPlatform.Wpf/Bridge/TradingBridgePublisher.cs`.

### 8.1.1. Тики публикации

```26:36:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Bridge/TradingBridgePublisher.cs
    private void OnStateChanged(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPublish < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        _lastPublish = now;
        Publish();
    }
```

- Источник тика — `ReadModel.StateChanged` (т.е. любое изменение `TradingPlatformState`).
- Дебаунс на 400 мс — независимо от того, как часто сыпется фид, snapshot обновляется максимум ~2.5 раза/сек.
- `Timer` каждые 3 с пишет `heartbeat.txt` (даже если state не менялся).

### 8.1.2. Что попадает в snapshot

`BuildSnapshot()` маппит `state` (in-memory) на `TradingPlatformSnapshotFile`. Важно понимать, **что НЕ публикуется**:

| Поле state | В snapshot? | Комментарий |
|---|---|---|
| `Account.Balance/Equity/FreeMargin/UsedMargin/Leverage` | ✅ | Все 5 |
| `Account.LeverageMode` | ❌ | Только числовое плечо |
| `Pnl.Today/Week/Month/AllTime` | ✅ |  |
| `Pnl.WinRate/SharpeRatio/MaxDrawdown` | ❌ | Только основные |
| `Risk.RiskLevel/DailyDrawdownPercent/ExposurePercent` | ✅ | Live-метрики |
| `Risk.SafeMode/EmergencyHalt/MaxLeverage` | ✅ |  |
| `Risk.MaxRiskPerTradePercent/MaxPositionSizeBtc/MaxExposurePercent/MaxDailyLossPercent/AutoShutdown/DefaultTakeProfitRrMultiplier/AutoApplyDefaultSlTp` | ❌ | **Не виден Hermes.Wpf** — это создаёт проблему для AssistantContext (Hermes не знает риск-лимиты). |
| `Hermes.State/ActiveStrategy/Confidence/CurrentReasoning/StrategyContext` | ✅ |  |
| `Positions[].Symbol/Side/Size/EntryPrice/MarkPrice/UnrealizedPnl` | ✅ | Без SL/TP, без LiquidationPrice, без RealizedPnl |
| `Orders[].Id/Symbol/Type/Side/Price/Quantity/Status/ReduceOnly` | ✅ | Без TriggerPrice, без CreatedAt |
| `Strategies[].Id/Name/Status/IsEnabled` | ✅ | Без RiskProfileLabel, без Description, без parameters |
| `Tickers[].Symbol/Price/ChangePercent24h` | ✅ | Без Volume24h, без InWatchlist |
| `Logs[]` | ⚠️ | Только последние **15** |
| `Journal[]` | ❌ | **Полностью не виден** через bridge. Для Hermes.Wpf — слепое пятно. |

> **Главное ограничение bridge'а**: `RecentLogs.Take(15)` — за окном при активной торговле «теряются» события. `TradingExperienceExporter` в `Hermes.Wpf` ловит только то, что попало в эти 15 строк за тик.

### 8.1.3. Время жизни и Dispose

- `TradingBridgePublisher` создаётся **в обоих** хост-приложениях: `Hermes.TradingPlatform.Wpf` и `Hermes.Wpf` (если хост запущен внутри Hermes? Нет — только из TradingPlatform.Wpf, см. `ServiceContainerFactory`).
- `Dispose` удаляет `heartbeat.txt` — это сигнал «терминал умер чисто».

## 8.2. Приём команд: `TradingBridgeCommandProcessor`

Файл: `Hermes.TradingPlatform.Wpf/Bridge/TradingBridgeCommandProcessor.cs`.

### 8.2.1. Поллинг

`Timer` с интервалом 1 секунда:

```30:62:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Bridge/TradingBridgeCommandProcessor.cs
    private void ProcessPending()
    {
        List<TradingPlatformCommand> commands;
        lock (_sync)
        {
            ...
            if (file?.Pending is not { Count: > 0 }) return;
            commands = file.Pending.ToList();
            File.WriteAllText(TradingBridgePaths.CommandsFile,
                JsonSerializer.Serialize(new TradingPlatformCommandFile(), JsonOptions));
        }

        foreach (var cmd in commands)
        {
            WpfThreading.RunOnUi(() => Execute(cmd));
        }
    }
```

- Под `lock` читает + **очищает** `commands.json`. После этого внутренняя БД содержит «нолёл» — это снижает риск re-execute при крэше во время обработки (за это платим тем, что если команда выполнится частично и крэшнется, она НЕ переисполнится — фиксированный trade-off).
- Каждая команда исполняется на UI-потоке (`WpfThreading.RunOnUi`). Это нужно потому, что многие state-операции внутри `TradingPlatformHost` рассчитаны на single-threaded mutation.

### 8.2.2. Поддерживаемые actions

| `cmd.Action` | Метод | Что делает |
|---|---|---|
| `place_order` | `ExecutePlaceOrder` | Market: подставляет последний `Tickers[symbol].Price`. Limit/Stop: требует `Price>0`. Вызывает `Exchange.PlaceOrder`. |
| `close_position` | `ExecuteClosePosition` | Резолвит symbol через `TradingSymbolResolver` (см. ниже). `Exchange.ClosePosition(symbol, qty?)`. |
| `cancel_order` | `ExecuteCancel` | `Exchange.TryCancelOrder(orderId)`. |
| `enable_strategy` / `set_strategy` | `ExecuteStrategy` | `host.SetStrategyEnabled(strategyId, enabled)`. |
| `emergency_stop` | `ExecuteEmergencyStop` | `host.EmergencyStop(reason)`. |
| `reset_account` | `ExecuteResetAccount` | `host.ResetPaperAccount()`. |
| прочее | `default` switch | `Success=false, Message="Unknown action: ..."` |

> ⚠️ `place_order` не пишет SL/TP. Это значит, что через bridge нельзя из CLI создать ордер с SL/TP — нужно отдельные place_order под `reduce_only=true` после открытия. В UI же `RiskValidator.AutoApplyDefaultSlTp` ставит SL/TP автоматически.

### 8.2.3. `TradingSymbolResolver`

Используется в `close_position` для нечёткого ввода:

```140:144:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Bridge/TradingBridgeCommandProcessor.cs
        var known = _host.StateStore.Snapshot.Tickers.Select(t => t.Symbol).ToList();
        var symbol = TradingSymbolResolver.Resolve(cmd.Symbol, known);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Fail(cmd, "close_position requires symbol");
        }
```

Это позволяет ассистенту прислать `"btc"` или `"bitcoin"`, и резолвер сам вернёт `"BTCUSDT"`. Полезно для chat-команд от Hermes.

### 8.2.4. Результаты

Все методы возвращают `TradingPlatformCommandResultFile`. `WriteResult` пишет `bridge/result-<guid>.json`. **Эти файлы НЕ удаляются** — за неделю работы их накопится много. Не критично (размер маленький), но архитектурно лучше иметь пруна (см. Findings).

## 8.3. Команды из Hermes.Wpf

В `Hermes.Wpf` есть аналог: `Hermes.Wpf/Services/TradingPlatformBridgeService.cs`. Это симметричный сервис, который:

1. Читает `snapshot.json` и `heartbeat.txt` (для отображения статуса в WPF).
2. Пишет команды в `commands.json` через атомарный `File.Replace` (`bridge/commands.json.tmp`).
3. Поддерживает `SnapshotUpdated` event (для подписчиков типа `TradingExperienceExporter`).
4. Может проверить «жив ли терминал» (`IsTerminalAlive` — heartbeat < 12 с).

> AssistantContext в Hermes.Wpf пробрасывает `IsTerminalAlive`, `Balance`, `OpenPositions[]`, `RiskLevel`, `HermesReasoning` в prompt — но НЕ пробрасывает риск-лимиты (они не в snapshot).

## 8.4. Hermes.TradingPlatform.Cli — терминал-клиент

Файл: `Hermes.TradingPlatform.Cli/Program.cs` — single-file .NET 8 console app.

### 8.4.1. Команды

| Команда | Параметры | Что делает |
|---|---|---|
| `status` | `--json` или `-j` | Печатает snapshot. Default — human-readable; с `--json` — raw JSON. Exit-codes: 0 OK, 2 нет файла, 3 невалидный snapshot. |
| `is-running` | — | Печатает `true`/`false`. Exit-codes: 0 жив, 2 мёртв или нет файла. |
| `enqueue '<json>'` | `<json>` — JSON `TradingPlatformCommand` одной строкой | Добавляет команду в `commands.json`. Печатает Guid команды. Exit-codes: 0 OK, 1 ошибка JSON. |
| `wait-result <guid>` | `--timeout=N` (default 15 сек) | Поллит `result-<guid>.json` каждые 300 мс. Exit-codes: 0 готово, 2 timeout. |
| `-h` / `--help` | — | Печатает help. |

### 8.4.2. Примеры использования

```bash
# Жив ли терминал
> hermes-trading-cli is-running
true

# Снимок (human-readable)
> hermes-trading-cli status
=== Hermes Trading Platform @ 2026-05-26T22:14:11Z ===
Feed: Connected (BinanceFutures)
Balance 102345.50 | Equity 102812.00 | Leverage 5.0x
PnL today +267.50 | Risk Conservative DD 0.3%
Hermes orchestrator: Idle | -
Reasoning: Awaiting next signal...

# Поставить рыночный ордер
> $id = hermes-trading-cli enqueue '{"action":"place_order","symbol":"BTCUSDT","side":"Buy","orderType":"Market","quantity":0.01}'
> hermes-trading-cli wait-result $id
{"CommandId":"...","Success":true,"Message":"Order o-1042 Filled (BTCUSDT Buy Market 0.01 RO=False)"}

# Аварийный стоп
> $id = hermes-trading-cli enqueue '{"action":"emergency_stop","requestedBy":"manual"}'
> hermes-trading-cli wait-result $id
```

### 8.4.3. Замечания по CLI

- **Default RequestedBy**: при `enqueue` всегда ставится `"Hermes.Wpf.Cli"`. В Hermes.Wpf свой `TradingPlatformBridgeService` ставит свой `RequestedBy="Hermes.Wpf"`.
- **Нет ретраев** на `wait-result` после timeout — нужно перезапускать. Файлы `result-*.json` остаются, и можно прочитать их вручную.
- **Нет атомарной записи** `enqueue` — `File.WriteAllText` напрямую. Если в этот момент `TradingBridgeCommandProcessor.ProcessPending` читает файл, может получиться race condition (хотя `JsonSerializer.Deserialize` либо успешно прочитает, либо вернёт null/throw). Обычно не критично из-за низкой частоты, но это slight risk.

## 8.5. Сценарий end-to-end

1. Hermes.Wpf (или Hermes.TradingPlatform.Cli):
   - Видит, что user в чате сказал «open btc long 0.05».
   - Парсит как intent → формирует `TradingPlatformCommand { Action="place_order", Symbol="BTCUSDT", Side="Buy", OrderType="Market", Quantity=0.05 }`.
   - Пишет в `bridge/commands.json`.
2. `TradingBridgeCommandProcessor` (внутри `Hermes.TradingPlatform.Wpf`):
   - Через ≤1 сек поднимает команду из `commands.json`, очищает файл.
   - Прокидывает на UI-поток → `Execute()`.
   - Вызывает `host.Exchange.PlaceOrder(...)` → `VirtualExchangeEngine` исполняет.
3. Внутри `VirtualExchangeEngine`:
   - Risk pre-check → если ОК → `OrderPlacedEvent` + `OrderFilledEvent` + `PositionUpdated`.
4. Подписчики:
   - `TradeJournalProjection` пишет `trade_journal.jsonl`.
   - `TradingStatePersistence` пишет `session-state.json` (immediate, не дебаунс).
   - `TradingBridgePublisher` обновляет `snapshot.json` (debounce 400 мс).
   - `EventLogProjection` добавляет log entries.
5. `TradingBridgeCommandProcessor.WriteResult` → `bridge/result-<guid>.json`.
6. `Hermes.Wpf` через `TradingPlatformBridgeService.SnapshotUpdated`:
   - Видит, что появился новый position и Pnl изменился.
   - `TradingExperienceExporter` (если включён) пишет в External Brain эпизод.
7. CLI `wait-result <guid>` → возвращает результат → ассистент видит «Order o-1042 Filled» и отвечает пользователю в чате.

## 8.6. Известные ограничения bridge'а

1. **Узкое поле для логов**: `RecentLogs.Take(15)` — экспортёр в Hermes.Wpf может пропустить fill, если за один debounce-tick их было >15.
2. **Журнал не виден через bridge**: Hermes.Wpf для тренировки не имеет доступа к полному `trade_journal.jsonl`. Решается либо прямым чтением файла (но это нарушает encapsulation), либо расширением snapshot'а отдельным `RecentJournal[]`.
3. **Order не несёт SL/TP**: `OrderSnapshot` не имеет `StopLoss`/`TakeProfit` (хотя `Order` в state хранит TriggerPrice для StopMarket/StopLimit, но не отдельно SL/TP). Hermes.Wpf при отображении должен делать догадки, что reduce-only ордера ниже/выше entry — это SL/TP.
4. **Bridge не транзакционен**: между записью команды и ответом нет гарантий, что промежуточные snapshot не были выкинуты — consumer должен сам корректировать состояние.
5. **Polling 1с** на read-командах — для интерактива нормально, но для high-frequency автоматики неоптимально.
6. **Атомарность записи** `commands.json` отсутствует — есть теоретическая race condition при одновременной записи из CLI и Hermes.Wpf.
