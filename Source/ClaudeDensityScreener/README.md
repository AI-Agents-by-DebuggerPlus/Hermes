# Hermes Density Screener — Python-слой

Детектор плотностей (order book walls + volume profile) для Binance Spot.
Пишет JSON snapshot для UI и агента (отдельный IPC, не Futures bridge).

## План

См. [`Docs/ClaudeDensityScreener/PLAN.md`](../../Docs/ClaudeDensityScreener/PLAN.md).

## Структура

```
density_screener/
  models.py, orderbook.py, binance_client.py, density.py
  storage.py, paths.py, cli.py
tests/
examples/simulate_run.py
```

## IPC

По умолчанию:

`%LocalAppData%\HermesDensity\bridge\density_snapshot.json`

Не смешивать с `%LocalAppData%\HermesTrading\bridge\snapshot.json`.

## Запуск

```bash
cd Source/ClaudeDensityScreener
pip install -r requirements.txt
python -m density_screener.cli --symbol BTCUSDT
```

Синтетика без сети:

```bash
PYTHONPATH=. python examples/simulate_run.py
PYTHONPATH=. pytest tests/ -v
```

## Поля snapshot (для агента)

`symbol`, `current_price`, `generated_at`, `levels[]`:
`price`, `side`, `volume`, `strength`, `source`, `distance_pct`, `eaten_ratio`, …

## Дальше (план)

B — live smoke Spot · C — read-only для агента · D — Futures endpoints · E — WPF heatmap · F — bounce strategy через risk bridge.
