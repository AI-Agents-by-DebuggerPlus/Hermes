"""End-to-end synthetic run (no network): walls → detect → JSON snapshot."""
from __future__ import annotations

import time
from pathlib import Path

from density_screener.density import (
    OrderBookDensityDetector,
    VolumeProfileBuilder,
    merge_with_profile,
)
from density_screener.models import OrderBookLevel, OrderBookSnapshot, ScreenerConfig
from density_screener.orderbook import LiveOrderBook
from density_screener.storage import read_json_snapshot, write_json_snapshot


def main() -> None:
    out = Path(__file__).resolve().parent / "synthetic_density_snapshot.json"
    cfg = ScreenerConfig(
        symbol="BTCUSDT",
        bucket_size_pct=0.5,
        strength_percentile=0.7,
        min_persistence_seconds=0.05,
    )
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(
        OrderBookSnapshot(
            symbol="BTCUSDT",
            last_update_id=1,
            bids=[
                OrderBookLevel(50000, 0.1),
                OrderBookLevel(49900, 0.2),
                OrderBookLevel(49800, 20.0),
            ],
            asks=[
                OrderBookLevel(50100, 0.1),
                OrderBookLevel(50200, 0.2),
                OrderBookLevel(50300, 20.0),
            ],
        )
    )
    det = OrderBookDensityDetector(cfg)
    vp = VolumeProfileBuilder(cfg)
    vp.add_trade(50300, 8.0)
    vp.add_trade(50300, 5.0)
    vp.add_trade(50050, 1.0)

    det.update(book, "BTCUSDT")
    time.sleep(0.06)
    levels = det.update(book, "BTCUSDT")
    mid = book.mid_price() or 50000
    width = mid * cfg.bucket_size_pct / 100.0
    levels = merge_with_profile(levels, vp.profile(width))
    write_json_snapshot(str(out), "BTCUSDT", mid, levels)
    data = read_json_snapshot(str(out))
    print(f"wrote {out}")
    print(f"levels={len(data['levels'])} mid={data['current_price']}")
    for lv in data["levels"]:
        print(
            f"  {lv['side']:3} @{lv['price']:.1f} vol={lv['volume']:.2f} "
            f"str={lv['strength']:.2f} src={lv['source']} dist={lv['distance_pct']:.3f}%"
        )


if __name__ == "__main__":
    main()
