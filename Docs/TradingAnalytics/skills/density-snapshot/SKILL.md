---
name: density-snapshot
description: "Read Hermes Density Screener JSON (order-book walls / volume profile). Use when user asks about плотности, стенки стакана, liquidity walls, HVN, bounce levels near price."
version: 0.1.0
metadata:
  hermes:
    tags: [trading, density, orderbook, binance, liquidity, hermes]
---

# Density Snapshot (read-only)

Live levels come from a **separate** Python process (Density Screener), not Futures Terminal.

## Quick check (Windows host)

```powershell
cd D:\Programming\AI_Agents\Hermes\Source\ClaudeDensityScreener
python scripts\summarize_density.py
```

## Paths

| File | Windows |
|------|---------|
| Snapshot | `%LocalAppData%\HermesDensity\bridge\density_snapshot.json` |
| Heartbeat | `%LocalAppData%\HermesDensity\bridge\heartbeat.txt` |

WSL: `/mnt/c/Users/<windows-user>/AppData/Local/HermesDensity/bridge/…`  
Project howto: Trading Analytics → `hermes/ecosystem/howto-density.md`.

## Procedure

1. Run `summarize_density.py` **or** read snapshot + heartbeat.
2. Heartbeat missing / age **> 15s** → screener down. Ask the user to start it via **Hermes.Wpf Launcher → Testing → Density Screener**
   (or `Source\ClaudeDensityScreener\scripts\run_density_screener.ps1`). Do not invent p5.js visuals.
3. If OK — answer in 2–4 sentences: mid price + top levels (`side`, `strength`, `distance_pct`, `source=both` preferred).
4. **No** trade placement from this skill. **No** dump of full JSON into chat or `MEMORY.md`.
