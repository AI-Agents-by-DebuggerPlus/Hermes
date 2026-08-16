# Hermes Density Screener — Python-слой

Детектор плотностей ликвидности (order book walls + volume profile) для Binance,
первый слой в интеграции скринера плотностей в Hermes. Отвечает за сбор данных,
детекцию плотностей и запись JSON-снапшота (file-based IPC), совместимого с
существующим паттерном Hermes.

## Структура

```
density_screener/
  models.py          # DensityLevel, OrderBookSnapshot, ScreenerConfig
  orderbook.py        # LiveOrderBook — реконструкция стакана (snapshot+diff), без сети
  binance_client.py   # REST snapshot + WS depth/aggTrade, OrderBookMaintainer (resync)
  density.py           # OrderBookDensityDetector, VolumeProfileBuilder, merge_with_profile
  storage.py            # write_json_snapshot (atomic), SupabaseHistoryWriter (опционально)
  cli.py                 # оркестратор: run() -> asyncio.gather(maintainer, trades, snapshot_loop)
tests/                   # unit-тесты на синтетике (orderbook, density, storage)
examples/simulate_run.py # end-to-end демо без сети
```

## Запуск

```bash
pip install -r requirements.txt
python -m density_screener.cli --symbol BTCUSDT --output ./density_snapshot.json
```

Опции: `--bucket-pct` (ширина бакета в % от mid), `--strength-percentile`,
`--persistence-sec` (анти-спуфинг: сколько плотность должна "прожить"),
`--eaten-threshold`, `--profile-window-sec`, `--snapshot-interval-sec`, `--depth-limit`.

## Как это работает

1. **OrderBookMaintainer** — держит live-стакан: REST snapshot + буферизация WS-диффов,
   применение по официальной схеме Binance (U/u sequence), автопересинхронизация при разрыве.
2. **OrderBookDensityDetector** — агрегирует объём в ценовые бакеты, порог по перцентилю,
   подтверждает плотность только после `min_persistence_seconds` (антиспуфинг), помечает
   "съеденные" уровни (`eaten_ratio`) и убирает их.
3. **VolumeProfileBuilder** — параллельно строит профиль по исполненным сделкам (`aggTrade`),
   его сложнее подделать, чем стакан.
4. **merge_with_profile** — апгрейдит `source: orderbook -> both`, когда стенка совпадает
   с зоной высокого исполненного объёма — это "сильная" плотность.
5. **write_json_snapshot** — атомарная запись JSON (tmp + rename), читается и WPF UI,
   и Hermes-агентом по тому же файлу.

## Тесты

```bash
pip install pytest
PYTHONPATH=. pytest tests/ -v
```

11 тестов покрывают: реконструкцию стакана (snapshot/diff/gap/resync), детекцию плотности
(персистентность, "поедание" уровня), volume profile (POC), merge с профилем, атомарную
запись/чтение снапшота.

Сетевой клиент (`binance_client.py`) не покрыт тестами в этой песочнице — нет доступа
к binance.com из текущего окружения. Логика REST/WS написана по официальной схеме
Binance (rest snapshot -> buffer diffs -> resync -> apply in order), но её стоит
прогнать у себя перед продакшеном.

## Дальше по плану

- Формат JSON уже содержит всё нужное для агента (`price`, `side`, `strength`, `source`,
  `distance_pct`, `eaten_ratio`) — следующий шаг: провайдер в `HermesAgentService`,
  читающий этот файл.
- WPF-визуализация: heatmap-лестница поверх того же JSON.
- Стратегийный модуль отработки отскоков поверх `DensityLevel` + существующий risk control.
