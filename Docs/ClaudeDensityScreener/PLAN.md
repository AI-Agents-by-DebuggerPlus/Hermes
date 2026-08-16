# План: Density Screener (Claude → Hermes)

**Дата:** 2026-08-16 (обновлено)  
**Источник:** `Docs/ClaudeDensityScreener/start.txt`, `1.txt`, код в `Source/ClaudeDensityScreener/`  
**Принцип:** отдельный процесс + file IPC; не ломать Futures bridge / risk path.

---

## Фазы

| Фаза | Что | Статус |
|------|-----|--------|
| **A** | Упаковать Python-пакет Claude, IPC-путь Hermes, тесты на синтетике | **готово** |
| **B** | Live smoke Spot Binance → JSON snapshot | **готово** |
| **C** | Индекс для агента (Trading Analytics / skill read-only) | **готово** |
| **D** | Futures Demo endpoints (`demo-fapi` / `demo-fstream`) | **готово** (`--market futures-demo`) |
| **E** | WPF heatmap (тонкий consumer JSON) | **готово** (`Hermes.DensityHeatmap`) |
| **F** | Стратегия отскоков → Futures bridge + RiskManager | **готово** (`bounce_strategy`, dry-run по умолчанию; bridge `ValidateOrder`) |

---

## Как тестировать (D–F)

1. **Launcher → Testing → Density Heatmap (WPF)** — UI по `density_snapshot.json`.
2. В heatmap: **Start screener (Spot)** или **Futures Demo**.
3. Либо консоль: `.\scripts\run_density_screener.ps1 -Market spot|futures-demo`.
4. Bounce dry-run: `.\scripts\run_bounce_strategy.ps1` (нужен живой snapshot).
5. Live bounce: Futures Demo терминал запущен + `.\scripts\run_bounce_strategy.ps1 -Live` (ордера через `HermesFutures/commands.json` → CapNotional + ValidateOrder).

## Не делать

- Писать плотности в `HermesTrading/bridge/snapshot.json`.
- Встраивать детектор в `MainViewModel` терминала.
- Обходить RiskManager при входах bounce-стратегии.
