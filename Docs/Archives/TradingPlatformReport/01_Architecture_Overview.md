# 01. Архитектурный обзор

## 1.1. Состав решения

`Hermes.TradingPlatform/Hermes.TradingPlatform.sln` объединяет 9 проектов на .NET 8 (`Hermes.TradingPlatform.Wpf` таргетит `net8.0-windows`, остальные — `net8.0`). Глобальные настройки заданы в `Directory.Build.props`: `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, версия продукта `0.6.0`.

| Проект | Тип | Ответственность |
|---|---|---|
| `Hermes.TradingPlatform.Shared` | classlib | Чистые DTO без зависимостей (`PlatformSettingsDto`, `RiskProfileSettingsDto`, мок-DTO для UI, контракты `bridge/`, `TradingSymbolResolver`, утилиты логов). Слой "без зависимостей". |
| `Hermes.TradingPlatform.Core` | classlib | Доменная модель (`TradingPlatformState`, `Order`, `Position`, `RiskProfile`, ...), интерфейсы (`IVirtualExchange`, `IRiskValidator`, `ITradingStateStore`, `ITradingStrategy`, `IMarketDataFeed`, `IHermesOrchestrator`), события платформы и `IEventBus`. Маппер `TradingUiMapper`. |
| `Hermes.TradingPlatform.Data` | classlib | Реализация `TradingStateStore`, проекции (`MarketTickProjection`, `EventLogProjection`, `TradeJournalProjection`), файловые хранилища (`PlatformSettingsFileStore`, `RiskProfileFileStore`, `StrategyParametersFileStore`, `TradingSessionStateFileStore`, `TradeJournalFileWriter`, `SqliteJournalStore`), seed (`InitialTradingSeed`). |
| `Hermes.TradingPlatform.Exchange` | classlib | `VirtualExchangeEngine` (paper-биржа), `BinanceFuturesMarketDataFeed`, `BinanceFuturesStreamParser`, `MockMarketDataFeed`, `PositionFillImpact`. |
| `Hermes.TradingPlatform.Risk` | classlib | `RiskValidator` (синхронные предордерные проверки) и `RiskCircuitBreaker` (асинхронная реакция на изменения состояния). |
| `Hermes.TradingPlatform.Strategies` | classlib | Контракт `ITradingStrategy`, `StrategyRunner`, `StrategyCooldown` и три встроенные стратегии (`MomentumStrategy`, `MeanReversionStrategy`, `LiquiditySweepStrategy`). |
| `Hermes.TradingPlatform.Orchestration` | classlib | `HermesOrchestrationService` — rule-based observer, обновляет `state.Hermes.{State, Mode, Confidence, CurrentReasoning, Decisions, Tasks}`. |
| `Hermes.TradingPlatform.Wpf` | **WinExe** | Корневое приложение (`AssemblyName=Hermes.TradingPlatform`). Composition root, страницы, контролы, ресурсы темы, мост к Hermes.Wpf, мини-ассистент OpenRouter. |
| `Hermes.TradingPlatform.Cli` | exe (net8.0) | Утилита терминала: `status`, `is-running`, `enqueue`, `wait-result`. Работает через файловый IPC. |

## 1.2. Граф зависимостей проектов

```
                       Shared
                         ▲
                         │
                       Core
              ┌──────────┼──────────────────────┐
              │          │                      │
            Data      Exchange   Risk   Strategies   Orchestration
              ▲          ▲       ▲          ▲              ▲
              └──────────┴───────┴──────────┴──────────────┘
                                  │
                                  Wpf
                                  │
                          Hermes.InAppAssistant(.Wpf)
                          (внешняя зависимость из ../)
```

- **Shared** — это **нижний уровень**, не ссылается ни на что. `Core` ссылается только на `Shared`.
- **Data / Exchange / Risk / Strategies / Orchestration** ссылаются на `Core` и `Shared` (через транзитивность).
- **Wpf** — единственный, кто видит **все** уровни сразу и собирает их в DI-контейнер.
- **Cli** ссылается только на `Shared` (точнее, на `Hermes.TradingPlatform.Shared.dll`) — он умеет читать/писать файлы bridge, но не знает про доменную модель.

См. `Hermes.TradingPlatform.Wpf/Hermes.TradingPlatform.Wpf.csproj` — там также явно подключён `..\..\Hermes.InAppAssistant.Wpf\Hermes.InAppAssistant.Wpf.csproj` и `Supabase 1.1.1` + `Microsoft.Extensions.DependencyInjection 8.0.1`.

## 1.3. Точки входа и lifecycle

### 1.3.1. WPF-приложение

`Hermes.TradingPlatform.Wpf/App.xaml.cs`:

```13:32:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/App.xaml.cs
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DwmDarkTitleBar.RegisterForAllWindows();
        Services = ServiceContainerFactory.Build();
        base.OnStartup(e);
    }
```

- Singleton-`ServiceProvider` строится в `OnStartup`, диспозится в `OnExit`.
- `MainWindow` указан в `App.xaml` (StartupUri), читает `MainViewModel` из DI.

### 1.3.2. Composition root — `ServiceContainerFactory.Build()`

`Hermes.TradingPlatform.Wpf/Services/Composition/ServiceContainerFactory.cs`:

```17:73:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Services/Composition/ServiceContainerFactory.cs
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<TradingPlatformHost>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<TradingPlatformHost>().EventBus);
        services.AddSingleton<ITradingStateStore>(sp => sp.GetRequiredService<TradingPlatformHost>().StateStore);
        services.AddSingleton<IVirtualExchange>(sp => sp.GetRequiredService<TradingPlatformHost>().Exchange);
        services.AddSingleton<IRiskValidator>(sp => sp.GetRequiredService<TradingPlatformHost>().RiskValidator);
        services.AddSingleton<TradingReadModel>(sp => sp.GetRequiredService<TradingPlatformHost>().ReadModel);

        services.AddSingleton<TradingBridgePublisher>();
        services.AddSingleton<TradingBridgeCommandProcessor>();
        // ... все ViewModels регистрируются как singleton (кроме JournalReplayService — transient) ...
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
```

**Ключевая идея:** `TradingPlatformHost` — это **agregate root** всей backend-инфраструктуры (`EventBus`, `StateStore`, `Exchange`, `RiskValidator`, `ReadModel`, `StrategyRunner`, `HermesOrchestrator`, файловые хранилища, market feed). Все остальные сервисы получают доступ к нему как к фасаду.

### 1.3.3. Конструктор `TradingPlatformHost`

`Hermes.TradingPlatform.Wpf/Services/TradingPlatformHost.cs`, `TradingPlatformHost()`:

1. Создаёт `InMemoryEventBus` и `TradingStateStore`.
2. Создаёт `TradingSessionStateFileStore` и пытается **восстановить состояние** (`TryLoad`). Иначе — `InitialTradingSeed.Create()`.
3. `RiskProfileFileStore.TryApplyTo(state.Risk)` — поверх состояния накладывается ранее сохранённый риск-профиль.
4. Загружает `PlatformSettingsDto`, парсит `MarketDataSource` (Mock vs BinanceFutures), читает `HermesOrchestrationEnabled`, применяет `Account.Leverage` через `ResolveEffectiveLeverage`.
5. Выбирает `JournalStore` — JSON (`TradeJournalFileWriter`) или SQLite (`SqliteJournalStore`) по `PlatformSettingsDto.JournalProvider`.
6. Создаёт `RiskValidator` и `VirtualExchangeEngine`. Если сессия восстановлена — `RestoreOrderSequence`.
7. Подключает **проекции**: `MarketTickProjection` (тикеры → позиции uPnL → equity), `EventLogProjection` (события → state.Logs), `TradeJournalProjection` (OrderFilled → state.Journal + journal-store).
8. Запускает фоновые сервисы: `TradingStatePersistence` (debounced autosave), `TradingSoundService` (звуки фиков), `RiskCircuitBreaker`.
9. Если сессия восстановлена — публикует системное событие "Session restored: ...".
10. Создаёт `TradingReadModel`, `StrategyParametersFileStore`, инстанцирует все 3 встроенные стратегии, применяет персистированные параметры, создаёт `StrategyRunner` (подписан на `MarketTickEvent`) и `HermesOrchestrationService` (подписан на `MarketTickEvent`, `StrategySignalEvent`, `RiskTriggeredEvent`, `OrderFilledEvent`, `OrderPlacedEvent`).

После конструктора `Start()` запускает выбранный `IMarketDataFeed`. Метод `RestartMarketFeed` останавливает предыдущий, перечитывает символы из `state.Tickers`, конструирует `BinanceFuturesMarketDataFeed` или `MockMarketDataFeed` и публикует системное лог-событие.

### 1.3.4. MainViewModel и навигация

`MainViewModel` (см. `ViewModels/Shell/MainViewModel.cs`) держит `Dictionary<NavigationPage, BaseViewModel>` для всех 13 страниц (см. `Navigation/NavigationPage.cs`):

```3:18:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Navigation/NavigationPage.cs
public enum NavigationPage
{
    Dashboard,
    Positions,
    Orders,
    Strategies,
    RiskManager,
    MarketWatch,
    Replay,
    Journal,
    Logs,
    Hermes,
    AccountSettings,
    Assistant,
    Settings,
}
```

`ViewLocator` (см. `Navigation/ViewLocator.cs`) и `PageContentPresenter` маршрутизируют ViewModel → View по имени. Активная страница хранится в `_activeNavPage`, `CurrentPage` (`BaseViewModel`), а навигация выполняется через `RelayCommand NavigateCommand` (DataTemplate в XAML).

Дополнительно в `MainViewModel`:
- Подписка `_host.FeedStatusChanged` → `UpdateConnectionStatus` (UI-метка статуса фида).
- Подписка `readModel.StateChanged` → `RefreshAccountSummary` (баланс, equity, PnL, open positions count для топ-бара).
- Подписка `TradeUiFeedback.MessageChanged` → `TradeStatusLine`.
- Создаёт **In-App Assistant** (`MiniAssistantViewModel`) с `OpenRouter` API-ключом из `PlatformSettingsDto.InAppAssistantOpenRouterApiKey`.

## 1.4. Жизненный цикл сессии

1. **Старт** → `App.OnStartup` → `ServiceContainerFactory.Build` → `TradingPlatformHost.ctor` → восстановление состояния → `MainViewModel.ctor` → `_host.Start()` → market feed подключается.
2. **Работа** → market ticks обновляют state → проекции пересчитывают позиции/uPnL/equity → ViewModel'и автоматически рефрешат UI через `TradingReadModel.StateChanged`. Параллельно `TradingBridgePublisher` пишет `bridge/snapshot.json` (дебаунс 400 мс) и `bridge/heartbeat.txt` (каждые 3 с). `TradingBridgeCommandProcessor` опрашивает `bridge/commands.json` раз в секунду и через `Dispatcher.RunOnUi` исполняет команды от Hermes.Wpf / CLI.
3. **Команды** (Place / Cancel / Modify / Close / Reset / EmergencyStop) проходят через `IVirtualExchange` или `TradingPlatformHost` → события на шине → проекции → State → UI.
4. **Стоп** → `App.OnExit` → `IServiceProvider.Dispose()` → `MainViewModel.Dispose()` → `TradingBridgePublisher` удаляет `heartbeat.txt`, `TradingPlatformHost.Dispose` останавливает фид и сохраняет финальный `session-state.json` через `TradingStatePersistence.SaveNow`.

## 1.5. Состояние backend-проектов: фазы

В `Hermes.TradingPlatform/Docs/` лежат маркдауны фазового плана (`Phase1_UI_Spec.md` … `Phase6_Hermes_Orchestration.md`, `TASKS.md`). На основании реализованного кода и комментариев в `MainViewModel.TopBarSubtitle = "Paper Trading · Virtual Exchange · Phase 6"` — все 6 фаз функционально присутствуют:

- **Phase 1** — UI/спецификация: страницы и контролы — ✅ есть (Dashboard, Positions, Orders, Strategies, RiskManager, MarketWatch, Replay, Journal, Logs, Hermes, AccountSettings, Assistant, Settings).
- **Phase 2** — состояние + события: `TradingStateStore`, `IEventBus`, проекции — ✅ работают, mock UI заменён живым ReadModel.
- **Phase 3** — Virtual Exchange: `VirtualExchangeEngine`, исполнение Market/Limit/Stop, Auto SL/TP, slippage, taker fee — ✅ есть.
- **Phase 4** — Binance Market Data: `BinanceFuturesMarketDataFeed` с WS @ticker + REST poll 24hr — ✅ работает.
- **Phase 5** — Strategy Execution: `StrategyRunner` + 3 стратегии + `StrategyCooldown` + `StrategyParametersFileStore` (hot-reload) — ✅.
- **Phase 6** — Hermes Orchestration: `HermesOrchestrationService` (rule-based observer, без LLM) — ✅.

## 1.6. Внешние зависимости

- **Microsoft.Data.Sqlite** — используется в `SqliteJournalStore` (через transitive в `Hermes.TradingPlatform.Data.csproj`; проверьте проект).
- **Supabase 1.1.1** — подключен только в WPF, ссылается из `SupabaseStartupNotifier` (стартовое уведомление о связности).
- **Microsoft.Extensions.DependencyInjection 8.0.1** — DI-контейнер.
- **Hermes.InAppAssistant / .Wpf** — мини-ассистент OpenRouter (отдельные проекты в корне репозитория).
- **System.Net.WebSockets / System.Net.Http** — для Binance WS+REST (нет сторонних SDK).

## 1.7. Ключевые директории файловой системы

| Где | Что |
|---|---|
| `%LocalAppData%/HermesTrading/session-state.json` | Полное восстановление сессии при старте |
| `%LocalAppData%/HermesTrading/trade_journal.jsonl` или `trade_journal.db` | Лента всех fills (по выбранному провайдеру) |
| `%LocalAppData%/HermesTrading/risk-profile.json` | Сохранённые риск-настройки |
| `%LocalAppData%/HermesTrading/platform-settings.json` | Источник данных, плечо, OpenRouter, журнал-провайдер |
| `%LocalAppData%/HermesTrading/strategy-parameters.json` | Per-strategy quantity/threshold/cooldown |
| `%LocalAppData%/HermesTrading/bridge/snapshot.json` | DTO для Hermes.Wpf |
| `%LocalAppData%/HermesTrading/bridge/commands.json` | Входящая очередь от Hermes.Wpf / CLI |
| `%LocalAppData%/HermesTrading/bridge/heartbeat.txt` | UTC ISO-8601 живости |
| `%LocalAppData%/HermesTrading/bridge/result-<guid>.json` | Результаты команд для `wait-result` |
| `D:/Programming/AI_Agents/Hermes/Logs/Hermes.TradingPlatform/trading_session_<stamp>.log` | Текстовые логи сессии (см. `TradingPlatformFileLogger` + `HermesLogsPaths`). Корень переопределяется `HERMES_LOGS_ROOT`. |

Очистка старых логов выполняется при создании логгера (`SessionLogPruner.PruneDirectory` оставляет последние 2 сессии).
