# 08. Настройки и конфигурация

## Hermes.Wpf — HermesSettings

Файл: `Hermes.Wpf/Models/HermesSettings.cs`  
Персистентность: `%LocalAppData%\HermesWpf\settings.json`

### Режим трейдинга

| Свойство | Default | Описание |
|----------|---------|----------|
| `TradingModeEnabled` | false | Активен режим трейдинга |
| `TradingSafetyRulesText` | см. ниже | Plain-text правила агента |
| `PersistedAgentRole` | Universal | Trader ↔ trading mode |

Дефолтный текст правил (если не меняли):

```
Маржа на сделку — не более 1% депозита ...
Максимальный убыток за день — 50 USDT ...
Только BTCUSDT и ETHUSDT ...
```

UI: **Settings → Трейдинг → Правила безопасности агента**  
(`SettingsWindow.xaml`, `SettingsViewModel.TradingSafetyRulesText`)

### Binance Demo Futures

| Свойство | Default | Описание |
|----------|---------|----------|
| `FuturesTerminalIntegrationEnabled` | true | Snapshot + команды в промпт |
| `FuturesTerminalAutoLaunch` | true | Запуск exe при trading mode |
| `FuturesTerminalExePath` | "" | Явный путь к exe (опц.) |

### Spot (опционально)

| Свойство | Описание |
|----------|----------|
| `SpotTerminalIntegrationEnabled` | Spot snapshot в промпт |
| `SpotTerminalAutoLaunch` | Автозапуск Spot Terminal |

### Trading Platform (отдельный продукт)

| Свойство | Описание |
|----------|----------|
| `TradingPlatformIntegrationEnabled` | Mock paper platform, **не** Demo Futures |
| `TradingPlatformAutoLaunchTerminal` | Hermes.TradingPlatform.exe |

### Опыт / экспорт

| Свойство | Описание |
|----------|----------|
| `TradingExperienceExportEnabled` | Эпизоды в External Brain |
| `TradingExperiencePnlThreshold` | Порог \|PnL\| для экспорта |
| `TradingExperienceDrawdownThreshold` | Порог просадки |

### Конкуренты режима

| Свойство | Эффект |
|----------|--------|
| `AssistantModeEnabled` | Блокирует trading |
| `EnglishTutorModeEnabled` | Снимает trader при входе |
| `HermesAgentPaused` | Чат без вызова Hermes |

## Терминал — PlatformSettings

Файл: `Hermes.BinanceDemoFuturesTerminal/Models/PlatformSettings.cs`  
Персистентность: рядом с exe / `TerminalPaths.SettingsFile`

### Риск-менеджер

| Свойство | Default | Описание |
|----------|---------|----------|
| `RiskManagementEnabled` | true | Вкл. проверки |
| `MaxOrderMarginPercent` | 1 | % депозита на сделку |
| `MaxTotalExposureUsdt` | 2000 | Суммарная экспозиция |
| `MaxOpenPositions` | 5 | Одновременные позиции |
| `MaxLeverage` | 20 | Лимит плеча (проверка) |
| `DefaultAgentOrderUsdt` | 50 | Объём по умолчанию для агента |

UI: **Настройки терминала → Риск-менеджер** (`SettingsWindow.xaml` в Futures Terminal).

### Ордер / UI

| Свойство | Описание |
|----------|----------|
| `DefaultLeverage` | Плечо по умолчанию |
| `QuantityInputMode` | UsdtOrderSize / UsdtInitialMargin / Contracts |
| `OrderEntryMode` | Limit / Market / Conditional |

## Согласованность Wpf ↔ Terminal

| Параметр | Где задаётся | Где видит агент |
|----------|--------------|-----------------|
| Max margin % | Terminal PlatformSettings | snapshot + промпт |
| Safety rules text | Hermes settings.json | только промпт |
| Default volume USDT | Terminal PlatformSettings | snapshot `DefaultAgentOrderUsdt` |
| Daily PnL | Terminal trade stats | snapshot `DailyRealizedPnlUsdt` |

**Важно:** правила агента **не** синхронизируются с External Brain — только `settings.json`.

## Включение для тестирования

1. Hermes.Wpf → «трейдинg» или роль Trader  
2. Settings → сохранить правила безопасности  
3. Запустить / автозапуск `Hermes.BinanceDemoFuturesTerminal.exe`  
4. API keys Demo в настройках терминала  
5. Риск-менеджер терминала — по желанию (для теста расхождения поставить 5%)

См. также: `Docs/Instructions/BinanceDemoFutures_Agent_Testing.ru.md`
