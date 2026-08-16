# Density Screener (плотности)

Отдельный Python-процесс: стакан + volume profile → JSON.  
UI: **Hermes.DensityHeatmap**. Bounce: `bounce_strategy` → Futures bridge (не в `HermesTrading` snapshot).

## Быстрый старт (визуал)

1. **Hermes.Wpf Launcher → Testing → Density Heatmap (WPF)**.
2. В heatmap: Start screener (Spot) или Futures Demo.
3. Либо консоль:
   ```powershell
   cd D:\Programming\AI_Agents\Hermes\Source\ClaudeDensityScreener
   .\scripts\run_density_screener.ps1              # Spot
   .\scripts\run_density_screener.ps1 -Market futures-demo
   ```
4. `python scripts\summarize_density.py` → `STATUS: screener OK`.
5. Trading Analytics чат: «Какие плотности по BTC?» (skill `density-snapshot`).

## Bounce (фаза F)

```powershell
.\scripts\run_bounce_strategy.ps1          # dry-run
.\scripts\run_bounce_strategy.ps1 -Live    # нужен запущенный Binance Demo Futures
```

Ордера идут в `%LocalAppData%\HermesFutures\bridge\commands.json` → terminal CapNotional + ValidateOrder.

## IPC

| Файл | Путь |
|------|------|
| Snapshot | `%LocalAppData%/HermesDensity/bridge/density_snapshot.json` (`market`, `levels[]`) |
| Heartbeat | `%LocalAppData%/HermesDensity/bridge/heartbeat.txt` (~15 с) |

## Агенту

1. Heartbeat stale → screener не запущен (предложить Launcher / Heatmap).
2. Топ 3–5 уровней: `side`, `distance_pct`, `source`, `strength`.
3. Не копируй snapshot в `MEMORY.md`. Не открывай сделки из чата (bounce — отдельный процесс).
