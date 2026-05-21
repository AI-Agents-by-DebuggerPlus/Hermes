# Phase 1 — UI-first (реализовано)

## Состав

- **Solution:** `Hermes.TradingPlatform.sln`
- **UI:** `Hermes.TradingPlatform.Wpf` — dark trading terminal, MVVM, mock data
- **Shared:** DTO для mock (`TradingMockModels.cs`)
- **Core / Exchange / Risk / Strategies / Data:** placeholders для фаз 2–6

## Страницы

| Sidebar | View | ViewModel |
|---------|------|-----------|
| Dashboard | `DashboardView` | `DashboardViewModel` |
| Positions | `PositionsView` | `PositionsViewModel` |
| Orders | `OrdersView` | `OrdersViewModel` |
| Strategies | `StrategiesView` | `StrategiesViewModel` |
| Risk Manager | `RiskManagerView` | `RiskManagerViewModel` |
| Market Watch | `MarketWatchView` | `MarketWatchViewModel` |
| Replay | `ReplayView` | `ReplayViewModel` |
| Logs | `LogsView` | `LogsViewModel` |
| Hermes | `HermesView` | `HermesViewModel` |
| Settings | `SettingsView` | `SettingsViewModel` |

## Reusable controls

`StatCard`, `PnlCard`, `PositionGrid`, `OrderGrid`, `SidebarButton`, `RiskIndicator`, `HermesStateWidget`

## Запуск

```powershell
dotnet build Hermes.TradingPlatform.sln -c Release
.\Hermes.TradingPlatform.Wpf\bin\Release\net8.0-windows\Hermes.TradingPlatform.exe
```

## Следующие фазы

См. `README.md` — Phase 2 domain models + event bus, без изменения layout UI.
