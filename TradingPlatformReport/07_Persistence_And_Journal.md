# 07. Персистентность и журналы

Все файловые артефакты лежат в **`%LocalAppData%/HermesTrading/`** (Windows: `C:/Users/<user>/AppData/Local/HermesTrading/`). На уровне кода это `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` + `"HermesTrading"`. Каждое хранилище создаёт директорию через `Directory.CreateDirectory` (idempotent).

| Файл | Класс | Контракт |
|---|---|---|
| `session-state.json` | `TradingSessionStateFileStore` | Полная сессия (account, pnl, positions, orders, tickers, strategies, journal[1000], logs[200], `NextOrderSequence`). Восстанавливается при старте. |
| `risk-profile.json` | `RiskProfileFileStore` | Только risk-конфиг (MaxDailyLoss, MaxRiskPerTrade, MaxPositionSizeBtc, MaxLeverage, MaxExposure, DefaultTakeProfitRrMultiplier, AutoApplyDefaultSlTp, SafeMode, AutoShutdown, EmergencyHalt). |
| `platform-settings.json` | `PlatformSettingsFileStore` | UI/системные настройки (MarketDataSource, HermesOrchestrationEnabled, TradingSoundsEnabled, OpenRouter ключ/модель, InitialAccountBalance, AccountLeverage, LeverageMode, JournalProvider). |
| `strategy-parameters.json` | `StrategyParametersFileStore` | Per-strategy параметры: `{StrategyId → {Quantity, ChangeThresholdPercent, CooldownSeconds}}`. |
| `trade_journal.jsonl` **или** `trade_journal.db` | `TradeJournalFileWriter` или `SqliteJournalStore` | Append-only лента всех fills. |
| `bridge/snapshot.json` | `TradingBridgePublisher` | DTO для Hermes.Wpf. См. `08_Bridge_And_CLI.md`. |
| `bridge/commands.json` | `TradingBridgeCommandProcessor` | Очередь команд. |
| `bridge/heartbeat.txt` | `TradingBridgePublisher` | ISO-8601 timestamp каждые 3 сек. |
| `bridge/result-<guid>.json` | `TradingBridgeCommandProcessor.WriteResult` | Результат одной команды для CLI `wait-result`. |

## 7.1. `TradingSessionStateFileStore` — главный файл сессии

Файл: `Hermes.TradingPlatform.Data/Persistence/TradingSessionStateFileStore.cs`. Формат: `TradingSessionStateFile.cs` (POCO с `Version=1`).

### 7.1.1. Что сохраняется

```40:55:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/TradingSessionStateFileStore.cs
    public void Save(TradingPlatformState state, int nextOrderSequence)
    {
        var file = MapToFile(state, nextOrderSequence);
        file.SavedAtUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
        else                       File.Move(temp, FilePath);
    }
```

- **Atomic write**: tmp + `File.Replace` гарантирует, что при крэше или power loss не получится half-written файл.
- **Caps**: `MaxOrders=500`, `MaxJournal=1000`, `MaxLogs=200` (см. константы наверху файла). Старые записи режутся при сохранении.
- **RiskProfile НЕ сохраняется** в session-state — он живёт в отдельном `risk-profile.json` (см. ниже). Это сознательное разделение: при сбросе аккаунта или повреждении session-state риск-настройки переживают.

### 7.1.2. Что восстанавливается

`TryLoad(out state, out nextOrderSequence)`:

1. Если `session-state.json` нет → return false (Host вызовет `InitialTradingSeed.Create()`).
2. Десериализация POCO → конвертация в `TradingPlatformState` (`ApplyFile`).
3. `MergeMissingSeedTickersAndStrategies` — если в сохранённом файле нет, например, ETHUSDT (старая версия) — добавит из seed. Это обеспечивает forward-compat при апгрейде платформы.
4. `nextOrderSequence` = `max(file.NextOrderSequence, InferNextOrderSequence(file.Orders))` — защита от неконсистентности (если кто-то отредактировал JSON вручную).

### 7.1.3. Когда сохраняется

`TradingStatePersistence` (`Data/Persistence/TradingStatePersistence.cs`) — подписан на:

- `_store.StateChanged` → **debounced** save через 400 мс (`ScheduleSave`).
- `OrderFilledEvent` → **немедленный** save (`SaveNow`).
- `OrderPlacedEvent` / `OrderCancelledEvent` → debounced.

```33:37:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/TradingStatePersistence.cs
        _store.StateChanged += (_, _) => ScheduleSave();
        bus.Subscribe<OrderFilledEvent>(_ => SaveNow());
        bus.Subscribe<OrderPlacedEvent>(_ => ScheduleSave());
        bus.Subscribe<OrderCancelledEvent>(_ => ScheduleSave());
```

`Dispose` тоже вызывает `SaveNow()`, чтобы при крэше через `App.OnExit` финальное состояние оказалось на диске.

> ⚠️ Если фид Binance выдаёт high-frequency тики, `StateChanged` тоже взводится часто. Дебаунс на 400 мс ограничивает запись до ~2.5 раз/сек, что приемлемо. На SQLite-режиме это менее критично, но JSON-журнал тоже пишет на каждый fill (см. ниже).

## 7.2. `RiskProfileFileStore`

Файл: `Hermes.TradingPlatform.Data/Persistence/RiskProfileFileStore.cs`.

Сохраняет только риск-конфиг (см. `RiskProfileFileModel` в том же файле). Default-values задают разумные значения:

```82:91:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/RiskProfileFileStore.cs
    private sealed class RiskProfileFileModel
    {
        public decimal MaxDailyLossPercent { get; set; } = 5m;
        public decimal MaxRiskPerTradePercent { get; set; } = 1m;
        public decimal MaxPositionSizeBtc { get; set; } = 0.5m;
        public decimal MaxLeverage { get; set; } = 5m;
        public decimal MaxExposurePercent { get; set; } = 50m;
        public decimal DefaultTakeProfitRrMultiplier { get; set; } = 2m;
        public bool AutoApplyDefaultSlTp { get; set; } = true;
        public bool SafeMode { get; set; } = true;
        public bool AutoShutdown { get; set; } = true;
        public bool EmergencyHalt { get; set; }
    }
```

`TryApplyTo(RiskProfile risk)` накладывает значения поверх существующего профиля (live-метрики `DailyDrawdownPercent`/`ExposurePercent`/`RiskLevel` не трогаются). Дополнительная защита: если в файле `MaxRiskPerTradePercent ≤ 0` или `DefaultTakeProfitRrMultiplier ≤ 0` — подставляется default.

Сохранение: `TradingPlatformHost.PersistRiskSettings` после любого изменения в Risk Manager UI.

## 7.3. `PlatformSettingsFileStore`

Файл: `Hermes.TradingPlatform.Data/Persistence/PlatformSettingsFileStore.cs`.

DTO: `PlatformSettingsDto.cs` (см. `02_Domain_Model.md`).

### 7.3.1. Миграции

При каждом `Load`:

1. `MigrateMarketDataToBinanceFutures` — legacy `Mock` → `BinanceFutures`.
2. `MigrateInAppAssistantLegacy` — старые поля `InAppAssistantGeminiApiKey` / `InAppAssistantOpenAiApiKey` → `InAppAssistantOpenRouterApiKey`, если соответствуют формату OpenRouter ключа (`sk-or-...`). Старые модели типа `gpt-4` / `gemini-1.5` мапятся в `"openrouter/free"`.
3. `TryImportOpenRouterFromHermesWpf` — если в `%LocalAppData%/HermesTrading/platform-settings.json` нет ключа, пытается читать `%AppData%/HermesWpf/settings.json` и подтянуть OpenRouter оттуда. Это **cross-app синхронизация ключа** между `Hermes.Wpf` и `Hermes.TradingPlatform`.

Загрузка terms-tolerant: при невалидном JSON возвращает дефолтный DTO.

### 7.3.2. Save семантика

`Save(PlatformSettingsDto)` — простой `WriteAllText(JsonSerializer.Serialize(...))`. Нет атомарного `File.Replace` (в отличие от session-state). Это **потенциальная уязвимость** при многократном изменении настроек, но критичные поля при потере перейдут на defaults.

## 7.4. `StrategyParametersFileStore`

Файл: `Hermes.TradingPlatform.Data/Persistence/StrategyParametersFileStore.cs`.

Хранит `Dictionary<string, StrategyParameters>` под `lock`. Атомарная запись через tmp + `File.Replace`.

API:
- `LoadAll()` → `Dictionary<string, StrategyParameters>` (case-insensitive).
- `Save(StrategyParameters)` — загружает все, обновляет одно по `StrategyId`, перезаписывает файл.
- `SaveAll(...)` — пакетная запись.

Используется только из `TradingPlatformHost.GetStrategyParameters` / `UpdateStrategyParameters` / `ApplyPersistedStrategyParameters`.

## 7.5. Журналы сделок: JSON vs SQLite

Контракт `IJournalStore`:

```5:16:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/IJournalStore.cs
public interface IJournalStore
{
    string Location { get; }
    void Append(TradeJournalEntry entry);
    void Clear();
    IReadOnlyList<TradeJournalEntry> LoadAll();
}
```

Выбор провайдера: `PlatformSettingsDto.JournalProvider = "Json" | "Sqlite"`. Default — `"Json"`. Меняется в `SettingsViewModel.JournalProvider` (требует перезапуска приложения — `TradingPlatformHost.JournalStore` создаётся однажды).

### 7.5.1. `TradeJournalFileWriter` (JSON Lines)

Файл: `Data/Persistence/TradeJournalFileWriter.cs`. Файл — `trade_journal.jsonl`.

- **Append-only**: `File.AppendAllText(filePath, line + NewLine, Utf8)` под глобальным `lock`.
- Строка — `JsonSerializer.Serialize(entry, WriteIndented=false, PropertyNameCaseInsensitive)`.
- `LoadAll` — читает `File.ReadLines` (lazy), пропускает blank и malformed строки.
- `Clear` — `WriteAllText("", Utf8)`.

**Плюсы**: совместимость, человеко-читаемость, легко grep/jq. **Минусы**: при больших объёмах `LoadAll` загружает всю историю в память; нет индекса по символу/датам.

### 7.5.2. `SqliteJournalStore`

Файл: `Data/Persistence/Sql/SqliteJournalStore.cs`. Файл — `trade_journal.db`.

Схема:

```146:165:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Persistence/Sql/SqliteJournalStore.cs
                CREATE TABLE IF NOT EXISTS journal_entries (
                    id              TEXT PRIMARY KEY,
                    ts_utc          INTEGER NOT NULL,
                    order_id        TEXT NOT NULL,
                    symbol          TEXT NOT NULL,
                    kind            TEXT NOT NULL,
                    side            TEXT NOT NULL,
                    quantity        REAL NOT NULL,
                    fill_price      REAL NOT NULL,
                    fee             REAL NOT NULL,
                    realized_pnl    REAL NOT NULL,
                    balance_before  REAL NOT NULL,
                    balance_after   REAL NOT NULL,
                    reduce_only     INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_journal_entries_ts ON journal_entries(ts_utc);
                CREATE INDEX IF NOT EXISTS ix_journal_entries_symbol ON journal_entries(symbol);
```

- **PRAGMA WAL + synchronous=NORMAL** — баланс между durability и producitivity.
- **`Quantity`/`FillPrice`/...` в `REAL`** — потеря precision относительно `decimal` (см. Findings).
- `LoadAll` сортирует по `ts_utc ASC, rowid ASC`.
- `Clear` — `DELETE FROM journal_entries`.

**Плюсы**: индексированные запросы, можно отдельно подключиться через `sqlite3` CLI или DB Browser. **Минусы**: `REAL` для денег — это `double`, что при больших AllTime PnL может вносить шумовые ошибки.

## 7.6. `JournalReplayService` — воспроизведение журнала

Файл: `Hermes.TradingPlatform.Wpf/Services/Replay/JournalReplayService.cs`.

Это **read-only** сервис: НЕ мутирует state, только эмитит `CurrentEntry` для UI. Используется в `ReplayViewModel`.

Поведение:
- `Reload(journalFilePath?)`:
  1. Берёт `state.Journal` (in-memory копию).
  2. Берёт `IJournalStore.LoadAll()` (если ошибка → пустой список).
  3. Если нет данных из store и передан `journalFilePath` (для JSON-режима — `trade_journal.jsonl`) → парсит файл напрямую.
  4. Выбирает **больший** из (in-memory, backup) и сортирует по `Timestamp ASC`.
- `Play / Pause / SetSpeed (1x/2x/4x) / Step / JumpTo` — управление через `DispatcherTimer` с интервалом `1000/Speed` мс.
- `CurrentEntry` — текущая запись для UI.
- `FormatProgress()` → `"N / Total · timestamp"`.

`ReplayViewModel.Reload` обновляет `ObservableCollection<TradeJournalEntry>` в UI-потоке через `WpfThreading.RunOnUi`.

## 7.7. Файловое расположение в LOC

```
%LocalAppData%/HermesTrading/
├── session-state.json           ← полная сессия (главный)
├── risk-profile.json            ← риск-конфиг
├── platform-settings.json       ← market data source, OpenRouter, ...
├── strategy-parameters.json     ← per-strategy params
├── trade_journal.jsonl          ← JSON провайдер (default)
├── trade_journal.db             ← SQLite провайдер (если включён)
└── bridge/
    ├── snapshot.json            ← для Hermes.Wpf
    ├── commands.json            ← очередь из Hermes.Wpf
    ├── heartbeat.txt            ← живость терминала
    └── result-{guid}.json       ← результат отдельной команды
```

```
D:/Programming/AI_Agents/Hermes/Logs/  (override через HERMES_LOGS_ROOT)
└── Hermes.TradingPlatform/
    └── trading_session_<yyyyMMdd_HHmmss>.log   ← текстовые логи (см. TradingPlatformFileLogger)
```

`SessionLogPruner` (см. `Hermes.TradingPlatform.Shared/Infrastructure/SessionLogPruner.cs`) при старте логгера сохраняет **только 2 последние** session-log'а в этой директории, остальные удаляет.

## 7.8. Восстановление: пример сценария

1. Пользователь работает, сделал 5 ордеров, 10 fills, баланс 102k.
2. Crash / kill / shutdown.
3. `TradingPlatformHost.Dispose` (если успел) → `_persistence.SaveNow` → `session-state.json` обновлён.
4. Пользователь запускает заново.
5. `TradingPlatformHost.ctor` → `SessionStateStore.TryLoad` → state восстановлен (positions, orders, journal[1000], logs[200], pnl, account).
6. `RiskProfileStore.TryApplyTo(state.Risk)` → риск-настройки восстановлены.
7. `Exchange.RestoreOrderSequence(orderSeq)` — `o-XXXX` продолжится корректно.
8. `EventBus.Publish(PlatformLogEvent { ... "Session restored" })` — оператор видит в логе.

**Что не восстанавливается:**
- `LiquidationPrice` остаётся seed-значением.
- `RiskCircuitBreaker._alreadyTripped = false` (новый объект) — если `EmergencyHalt=true` в файле, оператор должен снять его руками.

## 7.9. Сценарий сброса аккаунта (ResetPaperAccount)

`TradingPlatformHost.ResetPaperAccount` (вызывается из `AccountSettingsViewModel`):

```495:513:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Services/TradingPlatformHost.cs
    public void ResetPaperAccount()
    {
        var settings = PlatformSettingsStore.Load();
        var leverage = ResolveEffectiveLeverage(settings);
        var clean = InitialTradingSeed.CreateClean(settings.InitialAccountBalance, leverage);

        StateStore.Initialize(clean);
        Exchange.RestoreOrderSequence(1000);
        JournalStore.Clear();
        SessionStateStore.Save(StateStore.Snapshot, Exchange.NextOrderSequence);

        EventBus.Publish(new PlatformLogEvent(new PlatformLogEntry { ..., Message = $"Paper account reset: ..." }));
    }
```

- Использует `InitialTradingSeed.CreateClean(balance, leverage)` — чистый аккаунт с 3 тикерами и одной отключённой стратегией.
- `JournalStore.Clear()` — стирает **всю историю trade_journal** (jsonl или db). Это разрушающее действие, поэтому UI требует подтверждения.
- `SessionStateStore.Save` — сразу пишет на диск.
- **`RiskProfileStore` НЕ трогается** — риск-настройки сохраняются между сбросами (это полезно).

## 7.10. Подводные камни персистентности

1. **JSON формат** для session-state не имеет миграций (только `Version=1`). Если будет несовместимое изменение схемы — придётся писать миграции вручную или ronnet'нуть к defaults.
2. **`MergeMissingSeedTickersAndStrategies`** добавляет в state seed-объекты, **если их нет в файле**. Это полезно при upgrade, но также значит, что новые seed-тикеры/стратегии **появятся даже у пользователя**, который ранее их удалил (но в текущем UI удалять нельзя).
3. **SqliteJournalStore без миграций**: схема `CREATE TABLE IF NOT EXISTS` создаёт таблицу при первом запуске, но если в будущем добавятся поля — нужно ALTER TABLE.
4. **trade_journal.jsonl** растёт неограниченно (нет пруна). При длительной работе файл может стать большим (миллионы строк). `LoadAll` загружает всё в память — это узкое место для Replay при длинной истории.
5. **Atomic write** есть только в session-state и strategy-parameters. `platform-settings.json`, `risk-profile.json` и `trade_journal.jsonl` пишутся напрямую — теоретически уязвимы к power loss.
