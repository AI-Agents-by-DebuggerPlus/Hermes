# 09. WPF-оболочка и ViewModels

## 9.1. Скелет приложения

### 9.1.1. `App.xaml.cs`

`App.OnStartup` строит `IServiceProvider` через `ServiceContainerFactory.Build()` и регистрирует тёмную title-bar Win11 через `DwmDarkTitleBar.RegisterForAllWindows()`. `OnExit` диспозит контейнер — это триггерит `Dispose` у `MainViewModel`, `TradingPlatformHost`, `TradingBridgePublisher`, `TradingBridgeCommandProcessor`, `BinanceFuturesMarketDataFeed`, и т.д.

### 9.1.2. `MainWindow.xaml`

Шаблон трёх-колонной торговой консоли:

- **Sidebar (220px)**: бренд + `ListBox` с навигацией (`NavItemViewModel` × 13). Каждый item — `Button` со стилем `IsSelected` (data trigger через `NavItem.IsSelected`) + `HelpHintIcon` (новый кастомный контрол).
- **Top bar (56px)**: PageTitle, subtitle "Paper Trading · Virtual Exchange · Phase 6", connection-status badge, session clock.
- **AccountSummaryStrip**: тонкая полоса с Balance, Equity, PnL today/week/month, открытые позиции — кастомный контрол.
- **Content area**: `PageContentPresenter` — кастомный `ContentControl`, который маппит ViewModel → View через `ViewLocator`.

Особенность: **floating assistant**. `MiniAssistantPanel` рендерится с `Panel.ZIndex=40` поверх content area на всех табах, **кроме** `Assistant`. На вкладке Assistant панель встраивается в content layout (Grid.Row=1). Это даёт effect "Hermes всегда рядом", но не дублируется.

### 9.1.3. `ViewLocator`

```11:27:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/Navigation/ViewLocator.cs
    public static UserControl? Resolve(BaseViewModel? viewModel) => viewModel switch
    {
        DashboardViewModel => new DashboardView(),
        PositionsViewModel => new PositionsView(),
        OrdersViewModel => new OrdersView(),
        StrategiesViewModel => new StrategiesView(),
        RiskManagerViewModel => new RiskManagerView(),
        MarketWatchViewModel => new MarketWatchView(),
        ReplayViewModel => new ReplayView(),
        JournalViewModel => new JournalView(),
        LogsViewModel => new LogsView(),
        HermesViewModel => new HermesView(),
        AccountSettingsViewModel => new AccountSettingsView(),
        AssistantViewModel => new AssistantView(),
        SettingsViewModel => new SettingsView(),
        _ => null,
    };
```

Manual VM→View mapping без DataTemplate'ов. Чисто, типобезопасно, но требует обновления при каждом новом разделе.

## 9.2. `MainViewModel` (Shell/MainViewModel.cs)

### 9.2.1. Зависимости через ctor

```20:35:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/ViewModels/Shell/MainViewModel.cs
    public MainViewModel(
        TradingPlatformHost host,
        TradingBridgePublisher bridgePublisher,
        TradingBridgeCommandProcessor bridgeCommands,
        DashboardViewModel dashboard,
        PositionsViewModel positions,
        OrdersViewModel orders,
        StrategiesViewModel strategies,
        RiskManagerViewModel riskManager,
        MarketWatchViewModel marketWatch,
        ReplayViewModel replay,
        JournalViewModel journal,
        LogsViewModel logs,
        HermesViewModel hermes,
        AccountSettingsViewModel accountSettings,
        SettingsViewModel settings)
```

Все 12 страничных VM создаются через DI как **transient** и хранятся в `_pages` map. Это значит, что при открытии Assistant'а Dashboard остаётся живым в памяти — `StateChanged` подписки продолжают тригерить `Refresh` для всех страниц. Накладные расходы есть, но они минимальны (только `ObservableCollection<T>.Clear/Add` на каждом тике).

### 9.2.2. In-App Assistant

```46:60:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/ViewModels/Shell/MainViewModel.cs
        var assistantContext = new TradingInAppAssistantContextProvider(() => this);
        InAppAssistant = new MiniAssistantViewModel(
            new AppAssistantService(logger: new TradingAppAssistantLogger()),
            () =>
            {
                var s = _host.PlatformSettingsStore.Load();
                return new AppAssistantOptions
                {
                    ApplicationId = AppAssistantKnowledge.TradingPlatformId,
                    OpenRouterApiKey = s.InAppAssistantOpenRouterApiKey,
                    Model = s.InAppAssistantOpenRouterModel,
                };
            },
            assistantContext);
```

Используется отдельный shared-проект `Hermes.InAppAssistant` (см. также `Hermes.InAppAssistant.Wpf`). Это та же абстракция, что в основном Hermes.Wpf — `MiniAssistantViewModel` + `AppAssistantService` + контекст-провайдер. ApplicationId `TradingPlatformId` идентифицирует Trading Platform.

`TradingInAppAssistantContextProvider.GetLiveContextSnapshot()` отдаёт:

- Текущая страница (имя VM + title).
- Connection / feed status.
- Account.Equity, Balance.
- PnL today/week/month.
- Open positions count.
- Trade UI status line (последнее manual-уведомление).
- MarketDataSource (Binance/Mock).
- HermesOrchestrationEnabled flag.
- IsInAppAssistantConfigured.

**Что НЕ передаётся в snapshot for assistant:**
- Список positions (только count).
- Список orders.
- Risk profile / лимиты.
- Hermes reasoning / strategy context.
- Tickers, strategies.

Это **минималистичный контекст** — ассистент в Trading Platform пока в основном помогает с UI/настройками, а не с торговыми решениями (для торговых решений есть Hermes.Wpf ассистент с полным контекстом).

### 9.2.3. Поток событий в shell

```80:86:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/ViewModels/Shell/MainViewModel.cs
        _host.FeedStatusChanged += (_, _) => WpfThreading.RunOnUi(UpdateConnectionStatus);
        readModel.StateChanged += (_, _) => WpfThreading.RunOnUi(RefreshAccountSummary);
        TradeUiFeedback.Instance.MessageChanged += (_, _) =>
            WpfThreading.RunOnUi(() => TradeStatusLine = TradeUiFeedback.Instance.LastMessage);
```

Три источника:
1. `FeedStatusChanged` — мнемоника feed-а (Connected/Reconnecting/Disconnected).
2. `StateChanged` — любое изменение state → обновить Account/PnL/OpenPositionsCount в top-bar strip.
3. `TradeUiFeedback.MessageChanged` — последнее manual-сообщение (например, "Order o-1042 Filled @ 67400.50") для status line.

Все обновления маршалятся в UI-thread через `WpfThreading.RunOnUi`.

## 9.3. `TradingReadModel` — единая read-сторона

Файл: `Hermes.TradingPlatform.Wpf/Services/TradingReadModel.cs`.

Это **тонкий wrapper** над `ITradingStateStore.Snapshot`, который маппит на DTO через `TradingUiMapper`. Все страничные VM **читают только** через `ReadModel`, никогда напрямую `IStateStore` — это обеспечивает разделение слоёв (UI ↔ Domain).

Контракт (выдержка):
- `GetAccountSummary()` / `GetPnlSummary()` — top-bar.
- `GetOpenPositions()` — положение + связанные SL/TP ордера.
- `GetActiveOrders()` / `GetAllOrders()`.
- `GetRiskStatus()` / `GetRiskSettings()` — Risk Manager.
- `GetHermesStatus()` / `GetHermesReasoning()` / `GetHermesTasks()` / `GetHermesDecisions()`.
- `GetStrategies()`, `GetMarketWatch()`, `GetLogs()`, `GetJournal()`.

`StateChanged` event пробрасывается транзитом из `StateStore`.

> **Inversion: ReadModel — единственный путь чтения для UI.** Если Hermes.Wpf bridge-consumer хочет получить, скажем, RealizedPnl у Position — этого поля нет в bridge snapshot. Trading Platform's UI делает это через ReadModel/UiMapper, но bridge-снимок более ограниченный. Это ОК, потому что bridge — отдельная граница доменов; но это значит, что любое расширение UI **не** автоматически расширяет bridge — это надо делать руками.

## 9.4. Базовый `TradingPageViewModel`

```5:20:Hermes.TradingPlatform/Hermes.TradingPlatform.Wpf/ViewModels/Pages/TradingPageViewModel.cs
public abstract class TradingPageViewModel : BaseViewModel
{
    protected TradingPageViewModel(TradingReadModel readModel)
    {
        ReadModel = readModel;
        ReadModel.StateChanged += OnStateChanged;
    }

    protected TradingReadModel ReadModel { get; }

    protected virtual void OnStateChanged(object? sender, EventArgs e) =>
        WpfThreading.RunOnUi(Refresh);

    protected abstract void Refresh();
}
```

Все 12 страничных VM наследуются от него и получают **автообновление** при изменении state. `Refresh()` пишет `ObservableCollection.Clear() + Add()` (упрощённо — без diff'а). Для DataGrid'ов это сбрасывает выделение и виртуализацию, что заметно при большом списке (например, журнал на 1000 строк) — но в рамках paper-trading это приемлемо.

Один subtype делает не Clear/Add, а **дифф**:

- `StrategiesViewModel` (см. ниже).
- `MarketWatchViewModel` (по `_bySymbol` dictionary).

## 9.5. Обзор страниц

| Страница | Что показывает | Что может действовать |
|---|---|---|
| Dashboard | Account, PnL, Risk, Hermes status + OpenPositions + ActiveOrders cards | read-only |
| Positions | Список открытых + кнопки "Long/Short market", "Close one", "Close all" | `IVirtualExchange.PlaceOrder` / `ClosePosition` |
| Orders | Все ордера (Open/Filled/Cancelled/Rejected) + form для нового ордера + Modify/Cancel | `PlaceOrder` / `TryCancelOrder` / `ModifyOrder` |
| Strategies | Карточки стратегий с тогглом Enabled + Configure dialog | `SetStrategyEnabled` / `UpdateStrategyParameters` |
| RiskManager | Текущие риск-метрики + редактируемые лимиты + Emergency Stop | `PersistRiskSettings` / `EmergencyStop` |
| MarketWatch | Тикеры BTC/ETH/SOL + watchlist toggle + быстрый Long/Short/Close | `PlaceOrder` / `ClosePosition` |
| Replay | Слайдер по journal entries + play/pause/speed | read-only (`JournalReplayService` не мутирует state) |
| Journal | Полный список fills | read-only |
| Logs | Последние 200 platform-логов | read-only |
| Hermes | Hermes orchestration status, current reasoning, strategy context, Tasks[], Decisions[] | read-only |
| AccountSettings | Сброс баланса, плечо, leverage mode | `ResetPaperAccount` / `SetAccountLeverage` / `SetLeverageMode` |
| Assistant | OpenRouter ключ/модель + embedded `MiniAssistantPanel` | save assistant settings |
| Settings | MarketDataSource, Exchange mode, HermesOrchestration, sound, JournalProvider | `SetMarketDataSource` / `SetHermesOrchestrationEnabled` / `SetJournalProvider` |

### 9.5.1. Особенности отдельных VM

**`OrdersViewModel`** (`PlaceOrder` flow):
- Поля для ввода: Symbol, OrderType (Market/Limit/Stop), Side, Price, Quantity, ReduceOnly.
- `IsPriceRequired` — computed property на основе `OrderType != "Market"`.
- Market-order'ы берут цену из `ReadModel.GetMarketWatch().FirstOrDefault(Symbol).Price`.
- Использует `ManualTradeNotifier.ReportOrder` для сообщения в TradeUiFeedback (это попадёт в Hermes-assistant context line как "Trade UI status").

**`PositionsViewModel`**:
- `OpenMarket(side)` использует `ManualTradeNotifier.ResolveMarketPrice(ReadModel, symbol)` для последнего тика.
- Если нет ticker'а — выводит warning "wait for a market data tick" (защита от market-order без mark price).
- `CloseAll` итерирует по distinct symbols и закрывает каждый.

**`RiskManagerViewModel`**:
- 9 редактируемых полей: 6 decimals + 3 bool.
- `_suppressPersist` flag предотвращает recursive update при `LoadEditableSettings()`.
- При каждом setter вызывает `PersistEditableSettings()` → `_host.PersistRiskSettings(settings)` → live state + диск.
- `EmergencyStopCommand` — single-button trigger для `host.EmergencyStop`.
- Не имеет валидации на разумность значений (например, `MaxLeverage=100` или отрицательные числа). Только TryParse.

**`StrategiesViewModel`** (с дифф-обновлением):
- Не clear/add — итерирует, находит существующий card по Id, обновляет fields.
- `card.SyncingFromModel` flag предотвращает обратный вызов `IsEnabledChangedByUser`, когда apply'ится state от store.
- `ConfigureStrategyCommand` открывает `StrategyParametersDialog` — отдельный modal для редактирования Quantity / ChangeThresholdPercent / CooldownSeconds.

**`SettingsViewModel`**:
- `MarketDataMode` setter (с гвардом `_marketDataUiReady` на старте) реактивно вызывает `_host.SetMarketDataSource` — то есть **выбор Binance/Mock срабатывает сразу** без отдельной кнопки Apply (хотя кнопка ApplyMarketDataCommand тоже есть).
- `JournalProvider` тогл требует перезапуска (предупреждение в hint).
- `HermesIntegrationEnabled` / `TradingSoundsEnabled` — мгновенно через host.

**`AssistantViewModel`**:
- Привязан к OpenRouter (не Gemini).
- `PreviewSave` показывает "Ready to save: key <masked>, model <model>" по мере ввода.
- `SaveOpenRouterSettings` пишет в `platform-settings.json`.
- Использует `SettingsSaveFeedback.MaskSecret` для маскирования ключа в hint'е.

**`ReplayViewModel`**:
- `RelayCommand`s для всех action'ов (Play, Pause, Speed 1/2/4x, StepForward, StepBack, Reload, JumpTo).
- `Speed` computed свойство возвращает "1x" / "2x" / "4x".
- `MaxIndex` — `Entries.Count - 1` (для слайдера в XAML).
- При `OnReplayChanged` вызывает `RaiseAllReplayProperties` — рассылка всех нотификаций сразу.

## 9.6. Связь VM ↔ Domain

VM не имеют прямого доступа к `IEventBus` или `ITradingStateStore`. Они используют:

1. **`TradingReadModel`** — read.
2. **`IVirtualExchange`** — write (place/cancel/modify/close).
3. **`TradingPlatformHost`** — write для всего, что не ордера (стратегии, risk settings, market data, account leverage).
4. **`ManualTradeNotifier`** — single static class для парсинга и сообщений (используется `OrdersVM`, `PositionsVM`, `MarketWatchVM`).
5. **`TradeUiFeedback`** — singleton, который собирает последние сообщения для отображения в top-bar (один MessageChanged event).

`TradingPlatformHost` — **fat facade**: содержит state, exchange, persistence-сторы, feed-management, hermes-orchestration. Это упрощает DI (one big object), но усложняет testing (тяжело замокать). См. Findings.

## 9.7. Кастомные WPF контролы

`Hermes.TradingPlatform.Wpf/Controls/`:

| Контрол | Назначение |
|---|---|
| `AccountSummaryStrip.xaml` | Полоса Balance/Equity/PnL/Open positions в top-bar. |
| `HelpHintIcon.xaml` | Маленькая иконка "?" с тултипом RU-описания. Используется в sidebar (NavItem.ToolTipRu). |
| `KpiCard.xaml` | Универсальная карточка KPI (заголовок/значение/иконка). |
| `PageHeader.xaml` | Заголовок страницы. |
| `RiskBadge.xaml` | Цветной бейдж риска. |
| `RiskMeter.xaml` | Прогресс-бар риска. |

## 9.8. Стилистика

`Resources/TradingTheme.xaml` — централизованный набор brush'ей и стилей:
- `Brush.BgDeep`, `Brush.BgPanel`, `Brush.Border`, `Brush.Text`, `Brush.TextMuted`, `Brush.Accent`, `Brush.Positive`, `Brush.Negative`, `Brush.Warning`.
- `PageTitleStyle`, `KpiValueStyle`, и т.д.

Тема — тёмный finance trading look, hand-crafted (без сторонних UI-китов). Идёт в комплекте с `DwmDarkTitleBar` для тёмного title bar Windows 11.
