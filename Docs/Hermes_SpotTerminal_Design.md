# Hermes.SpotTerminal — Architecture & Integration Design

**Дата:** 2026-05-27  
**Статус:** Proposal (шаги 2–5 после аудита решения)  
**Сосуществует с:** `Hermes.TradingPlatform` (futures paper), `Hermes.Wpf` (orchestrator)

---

## 2. Структура папок и проектов

```
Hermes/
├── Hermes.slnx                          # + SpotTerminal.Shared, SpotTerminal.Cli (опционально)
├── Docs/
│   └── Hermes_SpotTerminal_Design.md    # этот файл
│
├── Hermes.Terminals.Shared/             # NEW — общий bridge + IPC (опционально, фаза 1b)
│   ├── Bridge/
│   │   ├── TerminalBridgePaths.cs
│   │   ├── UnifiedSnapshotFile.cs       # trading + spot + agent секции
│   │   └── TerminalCommandFile.cs
│   └── Hermes.Terminals.Shared.csproj
│
└── Hermes.SpotTerminal/
    ├── Directory.Build.props
    ├── Hermes.SpotTerminal.sln
    │
    ├── Hermes.SpotTerminal.Shared/           # DTO, bridge spot-секции, settings
    │   ├── Bridge/
    │   │   ├── SpotBridgePaths.cs
    │   │   ├── SpotTerminalSnapshotSection.cs
    │   │   ├── AgentSnapshotSection.cs
    │   │   └── SkillsSnapshotSection.cs
    │   ├── Settings/
    │   │   └── SpotPlatformSettingsDto.cs
    │   └── Mock/                             # UI DTO (как TradingPlatform.Shared.Mock)
    │
    ├── Hermes.SpotTerminal.Core/              # Domain + abstractions + events
    │   ├── Domain/
    │   │   ├── SpotPlatformState.cs
    │   │   ├── SpotAccount.cs
    │   │   ├── SpotBalance.cs
    │   │   ├── SpotOrder.cs
    │   │   ├── SpotPosition.cs               # optional (spot = balances + open orders)
    │   │   ├── MarketTicker.cs
    │   │   ├── KlineBar.cs
    │   │   ├── OrderBookLevel.cs
    │   │   ├── AgentSession.cs
    │   │   ├── AgentEvent.cs                 # base / discriminated
    │   │   ├── Skill.cs
    │   │   ├── SkillLifecycle.cs             # Draft | Backtest | Approved | Archived
    │   │   └── LearningJournalEntry.cs
    │   ├── Abstractions/
    │   │   ├── IEventBus.cs
    │   │   ├── ISpotStateStore.cs
    │   │   ├── IMarketDataFeed.cs            # miniTicker, kline, trade, depth
    │   │   ├── ISpotExecutionGateway.cs      # place/cancel/account (testnet or virtual)
    │   │   ├── IAgentMonitoringService.cs
    │   │   ├── ISkillRepository.cs
    │   │   └── ILearningJournalStore.cs
    │   ├── Events/
    │   │   ├── InMemoryEventBus.cs
    │   │   ├── MarketStreamEvents.cs
    │   │   ├── TradingEvents.cs
    │   │   └── AgentEvents.cs                # AgentThought, AgentDecision, ...
    │   └── Enums/
    │       ├── ExecutionMode.cs              # Virtual | SpotDemo
    │       └── AgentEventKind.cs
    │
    ├── Hermes.SpotTerminal.Exchange/           # Binance.Net + Virtual
    │   ├── Binance/
    │   │   ├── BinanceSpotTestnetClientFactory.cs
    │   │   ├── BinanceSpotMarketDataFeed.cs    # WS: miniTicker, kline, trade, depth
    │   │   ├── BinanceSpotExecutionGateway.cs  # REST: orders, account, balances
    │   │   └── BinanceSymbolNormalizer.cs
    │   ├── Virtual/
    │   │   ├── VirtualSpotExchange.cs
    │   │   └── VirtualSpotMarketDataFeed.cs
    │   └── ExchangeModule.cs                 # IPlatformModule registration
    │
    ├── Hermes.SpotTerminal.Agent/              # Agent loop + monitoring
    │   ├── AgentMonitoringService.cs
    │   ├── AgentSessionManager.cs
    │   ├── AgentEventProjector.cs              # → state.Logs + persistence
    │   └── Skills/
    │       ├── SkillCatalog.cs                 # Initial skills seed
    │       ├── SkillLifecycleService.cs        # Draft → Backtest → Approved
    │       └── SkillBacktestRunner.cs
    │
    ├── Hermes.SpotTerminal.Data/               # Persistence + projections
    │   ├── Persistence/
    │   │   ├── AtomicJsonFileStore.cs          # tmp + File.Replace (shared util)
    │   │   ├── SpotSessionStateFileStore.cs
    │   │   ├── SpotPlatformSettingsFileStore.cs
    │   │   ├── AgentEventsJsonlStore.cs
    │   │   ├── AgentEventsSqliteStore.cs
    │   │   ├── SkillsJsonStore.cs
    │   │   └── LearningJournalStore.cs
    │   ├── Projections/
    │   │   ├── MarketTickProjection.cs
    │   │   ├── EventLogProjection.cs
    │   │   └── TradeJournalProjection.cs
    │   └── Sql/
    │       └── SqliteSchemaMigrator.cs
    │
    ├── Hermes.SpotTerminal.Wpf/                # Host UI
    │   ├── App.xaml.cs
    │   ├── Services/
    │   │   ├── Composition/ServiceContainerFactory.cs
    │   │   ├── SpotTerminalHost.cs             # аналог TradingPlatformHost
    │   │   ├── SpotReadModel.cs
    │   │   └── SpotPlatformFileLogger.cs
    │   ├── Bridge/
    │   │   ├── SpotBridgePublisher.cs
    │   │   └── SpotBridgeCommandProcessor.cs
    │   ├── ViewModels/
    │   │   ├── Shell/MainViewModel.cs
    │   │   └── Pages/
    │   │       ├── DashboardViewModel.cs
    │   │       ├── MarketWatchViewModel.cs
    │   │       ├── OrdersViewModel.cs
    │   │       ├── BalancesViewModel.cs
    │   │       ├── AgentMonitorViewModel.cs
    │   │       ├── SkillsViewModel.cs
    │   │       ├── LearningJournalViewModel.cs
    │   │       ├── LogsViewModel.cs            # фильтр Source=Agent
    │   │       └── SettingsViewModel.cs
    │   ├── Views/ ...
    │   └── Navigation/ViewLocator.cs
    │
    └── Hermes.SpotTerminal.Cli/                # status | enqueue | is-running
        └── Program.cs
```

### Граф зависимостей проектов

```
SpotTerminal.Wpf
  → SpotTerminal.Agent
  → SpotTerminal.Data
  → SpotTerminal.Exchange
  → SpotTerminal.Core
  → SpotTerminal.Shared
  → Hermes.InAppAssistant.Wpf          (опционально, как в TradingPlatform)
  → Hermes.Terminals.Shared            (фаза 1b, unified bridge)

SpotTerminal.Cli
  → SpotTerminal.Shared
  → Hermes.Terminals.Shared            (или SpotTerminal.Shared.Bridge only)

SpotTerminal.Agent
  → SpotTerminal.Core
  → SpotTerminal.Data                  (только interfaces в Core; impl в Data)

SpotTerminal.Exchange
  → SpotTerminal.Core
  → Binance.Net

SpotTerminal.Data
  → SpotTerminal.Core
  → Microsoft.Data.Sqlite
```

### Data root (отдельно от Trading Platform)

```
%LocalAppData%/HermesSpot/
├── session-state.json
├── platform-settings.json
├── api-credentials.json              # testnet keys (encrypted optional, phase 2)
├── agent_events.jsonl                # или agent_events.db
├── skills/
│   ├── catalog.json
│   └── {skillId}/
│       ├── manifest.json
│       ├── draft/
│       └── backtest-results/
├── learning_journal.jsonl
└── bridge/
    ├── snapshot.json                 # unified или spot-only (см. §5)
    ├── commands.json
    ├── heartbeat.txt
    └── result-{guid}.json
```

---

## 3. NuGet пакеты

| Проект | Package | Версия (ориентир) | Назначение |
|--------|---------|-------------------|------------|
| **SpotTerminal.Exchange** | `Binance.Net` | latest stable 9.x | Spot Testnet REST + WS |
| **SpotTerminal.Exchange** | `CryptoExchange.Net` | (transitive) | Base для Binance.Net |
| **SpotTerminal.Wpf** | `Microsoft.Extensions.DependencyInjection` | 8.0.x | Composition root |
| **SpotTerminal.Data** | `Microsoft.Data.Sqlite` | 8.0.x | agent_events.db, skills index |
| **SpotTerminal.Wpf** | `Microsoft.Extensions.Logging` | 8.0.x | Structured logs (optional) |
| **SpotTerminal.Agent** | — | — | Без LLM SDK на старте; Hermes.Wpf — brain |
| **Hermes.Terminals.Shared** | — | — | Только BCL |

> **Не добавлять** в Core: Binance.Net (только Exchange).  
> **Не дублировать** Supabase в SpotTerminal, если не нужен отдельный relay.

### Binance.Net — что использовать

- `BinanceRestClient` + `BinanceSocketClient` с `BinanceEnvironment.Testnet` (Spot).
- Streams: `SubscribeToTickerUpdatesAsync`, `SubscribeToKlineUpdatesAsync`, `SubscribeToTradeUpdatesAsync`, `SubscribeToOrderBookUpdatesAsync`.
- REST: `SpotApi.Trading.PlaceOrderAsync`, `CancelOrderAsync`, `GetAccountInfoAsync`, `GetBalancesAsync`.

---

## 4. Основные модели (Domain)

### 4.1. `SpotPlatformState` (aggregate root)

```csharp
public sealed class SpotPlatformState
{
    public ExecutionMode Mode { get; set; }           // Virtual | SpotDemo
    public SpotAccount Account { get; set; }
    public IReadOnlyList<SpotBalance> Balances { get; set; }
    public IReadOnlyList<SpotOrder> OpenOrders { get; set; }
    public IReadOnlyList<MarketTicker> Tickers { get; set; }
    public AgentSession Agent { get; set; }
    public IReadOnlyList<Skill> Skills { get; set; }
    public IReadOnlyList<PlatformLogEntry> Logs { get; set; }   // EventType + Source filter
    public IReadOnlyList<LearningJournalEntry> LearningJournal { get; set; }
}
```

### 4.2. Agent events (discriminated)

```csharp
public enum AgentEventKind
{
    Thought,
    Decision,
    ToolCall,
    TradeExecuted,
    StrategyStep,
}

public sealed record AgentEvent(
    Guid Id,
    DateTimeOffset TimestampUtc,
    AgentEventKind Kind,
    string SessionId,
    string? Symbol,
    string Summary,              // short line for Logs UI
    string PayloadJson);           // full structured body for DB/JSONL
```

Typed payloads (optional records, serialized in `PayloadJson`):

- `AgentThoughtPayload` — reasoning text, confidence
- `AgentDecisionPayload` — action, rationale, rejected?
- `AgentToolCallPayload` — tool name, args, result
- `AgentTradeExecutedPayload` — order id, side, qty, price, fee
- `AgentStrategyStepPayload` — strategy id, step name, metrics

### 4.3. `AgentSession`

```csharp
public sealed class AgentSession
{
    public string Id { get; set; }
    public string State { get; set; }              // Idle | Running | Paused | Halted
    public string? ActiveSkillId { get; set; }
    public string CurrentThought { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? LastEventAtUtc { get; set; }
}
```

### 4.4. `Skill` + lifecycle

```csharp
public enum SkillStatus { Draft, Backtesting, Approved, Archived }

public sealed class Skill
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public SkillStatus Status { get; set; }
    public bool IsInitial { get; set; }            // seed skills
    public string? ScriptPath { get; set; }
    public string? ParametersJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public BacktestSummary? LastBacktest { get; set; }
}

public sealed record BacktestSummary(
    DateTimeOffset RunAtUtc,
    int Trades,
    decimal NetPnl,
    decimal MaxDrawdownPercent,
    bool PassedThreshold);
```

Lifecycle transitions (`SkillLifecycleService`):

1. **Draft** — created by agent or user; editable.
2. **Backtesting** — `SkillBacktestRunner` on historical klines or journal replay.
3. **Approved** — manual or auto if metrics pass; eligible for live agent.
4. **Archived** — disabled, kept for history.

### 4.5. `LearningJournalEntry`

```csharp
public sealed class LearningJournalEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string Category { get; set; }           // Trade | Mistake | Insight | Skill
    public string Title { get; set; }
    public string Body { get; set; }
    public IReadOnlyList<string> Tags { get; set; }
    public string? RelatedSkillId { get; set; }
    public string? Symbol { get; set; }
}
```

### 4.6. Bridge DTO sections (Shared)

```csharp
// Расширение unified snapshot (см. §5)
public sealed class AgentSnapshotSection
{
    public string SessionState { get; init; }
    public string? ActiveSkillId { get; init; }
    public string CurrentThought { get; init; }
    public IReadOnlyList<AgentEventSnapshot> RecentEvents { get; init; }
}

public sealed class SpotTerminalSnapshotSection
{
    public string ExecutionMode { get; init; }     // Virtual | SpotDemo
    public string FeedStatus { get; init; }
    public IReadOnlyList<BalanceSnapshot> Balances { get; init; }
    public IReadOnlyList<SpotOrderSnapshot> OpenOrders { get; init; }
    public IReadOnlyList<MarketTickerSnapshot> Tickers { get; init; }
}

public sealed class SkillsSnapshotSection
{
    public IReadOnlyList<SkillSnapshot> Skills { get; init; }
    public int DraftCount { get; init; }
    public int ApprovedCount { get; init; }
}
```

---

## 5. Интеграция в главное решение

### 5.1. `Hermes.SpotTerminal.sln` (локальный)

8 проектов — зеркало TradingPlatform без Risk/Strategies/Orchestration, плюс **Agent**.

### 5.2. `Hermes.slnx` (корень)

Добавить:

```xml
<Project Path="Hermes.SpotTerminal/Hermes.SpotTerminal.Shared/Hermes.SpotTerminal.Shared.csproj" />
<!-- Опционально для IDE: -->
<Project Path="Hermes.Terminals.Shared/Hermes.Terminals.Shared.csproj" />
```

`Hermes.Wpf.csproj`:

```xml
<ProjectReference Include="..\Hermes.SpotTerminal\Hermes.SpotTerminal.Shared\Hermes.SpotTerminal.Shared.csproj" />
<!-- или Hermes.Terminals.Shared после унификации bridge -->
```

Post-build (как TradingPlatform CLI):

```xml
<Target Name="CopySpotTerminalCliToWpfOut" AfterTargets="Build">
  <!-- Hermes.SpotTerminal.Cli.dll → $(OutDir) -->
</Target>
```

### 5.3. `Hermes.Wpf` — новые сервисы

| Сервис | Роль |
|--------|------|
| `SpotTerminalBridgeService` | Read snapshot, enqueue commands, `SnapshotUpdated` |
| `SpotAgentContextBuilder` | Inject `agent` + `spotTerminal` + `skills` into Hermes prompt |
| `SpotExperienceExporter` | Learning journal → External Brain (опционально) |

Settings (`HermesSettings`):

```csharp
public bool SpotTerminalIntegrationEnabled { get; set; } = true;
public bool SpotTerminalAutoLaunch { get; set; } = true;
public string SpotTerminalExePath { get; set; } = "";
public string SpotTerminalCliPath { get; set; } = "";
```

### 5.4. Coexistence checklist

- [ ] Разные `%LocalAppData%` roots: `HermesTrading` vs `HermesSpot`
- [ ] Разные exe: `Hermes.TradingPlatform.exe` vs `Hermes.SpotTerminal.exe`
- [ ] Hermes.Wpf может держать **оба** bridge одновременно (futures + spot)
- [ ] Роли: `AgentRole.Trader` может переключать контекст по активному терминалу

---

## 6. Расширение файлового bridge

### 6.1. Рекомендуемый подход: **Unified snapshot v2**

Один файл `bridge/snapshot.json` с **обратной совместимостью**:

```json
{
  "schemaVersion": 2,
  "timestampUtc": "...",
  "tradingPlatform": { /* существующий TradingPlatformSnapshotFile */ },
  "spotTerminal": { /* SpotTerminalSnapshotSection */ },
  "agent": { /* AgentSnapshotSection */ },
  "skills": { /* SkillsSnapshotSection */ }
}
```

- `Hermes.TradingPlatform` publisher: заполняет только `tradingPlatform` (остальное null/omit).
- `Hermes.SpotTerminal` publisher: заполняет `spotTerminal`, `agent`, `skills`.
- `Hermes.Wpf`: десериализует с `PropertyNameCaseInsensitive`; игнорирует отсутствующие секции.

**Альтернатива (проще старт):** отдельный `bridge/spot-snapshot.json` — меньше координации между процессами, но два poll в Wpf.

### 6.2. Кто пишет что

| Файл | Writer | Reader |
|------|--------|--------|
| `snapshot.json` | TP.Wpf **или** Spot.Wpf **или** агрегатор | Hermes.Wpf |
| `commands.json` | Hermes.Wpf, Cli | TP.Wpf + Spot.Wpf (разделить очереди phase 2) |
| `heartbeat.txt` | активный терминал | Hermes.Wpf `IsTerminalAlive` |

**Phase 2 commands:** `commands.json` → `{ "trading": [...], "spot": [...] }` чтобы оба терминала не съедали чужие команды.

### 6.3. Agent logs в общем окне Logs

В `SpotPlatformState.Logs` (`PlatformLogEntry`):

```csharp
public string Source { get; set; }    // "Agent" | "Exchange" | "MarketData" | "System"
public string EventType { get; set; } // "AgentThought" | "AgentDecision" | ...
```

`LogsViewModel`:

- Filter: `All | Agent | Trading | System`
- Default для SpotTerminal UI: показать Agent filter на странице Agent Monitor; общая Logs — все sources.

Bridge `RecentLogs` — добавить поле `Source` в `LogSnapshot` (уже есть в TP).

### 6.4. `AgentMonitoringService` API

```csharp
public interface IAgentMonitoringService
{
    void PublishThought(string summary, object? payload = null);
    void PublishDecision(string summary, object? payload = null);
    void PublishToolCall(string tool, object args, object? result = null);
    void PublishTradeExecuted(SpotOrder order, decimal? realizedPnl = null);
    void PublishStrategyStep(string strategyId, string step, object? metrics = null);
}
```

Implementation:

1. Build `AgentEvent` → `IEventBus.Publish(AgentEventRecorded)`.
2. `AgentEventProjector` → append SQLite/JSONL + `state.Logs` (cap 200).
3. `SpotBridgePublisher` → include in `agent.RecentEvents` (last 20).

---

## 7. Режимы Virtual ↔ SpotDemo

| | Virtual | SpotDemo |
|---|---------|----------|
| Market data | `VirtualSpotMarketDataFeed` | Binance.Net WS testnet |
| Execution | `VirtualSpotExchange` | `BinanceSpotExecutionGateway` |
| Keys | не нужны | `api-credentials.json` |
| State persist | `session-state.json` | + sync balances from REST on start |

`SpotTerminalHost.SetExecutionMode(ExecutionMode mode)` — stop feeds, swap gateway, restore session.

---

## 8. Фазы реализации (рекомендуемый порядок)

| Фаза | Deliverable |
|------|-------------|
| **0** | Shared bridge DTO + paths; документация |
| **1** | Core + Virtual exchange + Wpf shell + Logs |
| **2** | Binance.Net Spot Testnet (market + execution) |
| **3** | AgentMonitoringService + Agent UI |
| **4** | Skills lifecycle + Skills UI |
| **5** | Learning journal + Hermes.Wpf integration |
| **6** | Unified snapshot v2 + Hermes.Wpf prompt blocks |

---

## 9. Отличия от Hermes.TradingPlatform (намеренные)

| TradingPlatform | SpotTerminal |
|-----------------|----------------|
| USDT-M Futures paper | Spot Testnet (real API) + Virtual |
| Positions + margin | Balances + spot orders |
| RiskValidator + CircuitBreaker | Risk phase 2 (optional lighter limits) |
| StrategyRunner (3 strategies) | Agent skills + backtest |
| HermesOrchestrationService (rules) | AgentMonitoringService (events) |
| `HermesTrading` data dir | `HermesSpot` data dir |

---

*Конец документа. Следующий шаг по запросу: scaffold проектов (csproj + пустые классы) или Phase 0 bridge extension.*
