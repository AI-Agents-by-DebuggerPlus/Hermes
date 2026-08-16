"""Unit tests for density_screener (synthetic data, no network)."""
from __future__ import annotations

import time
from pathlib import Path

import pytest

from density_screener.density import (
    OrderBookDensityDetector,
    VolumeProfileBuilder,
    bucketize,
    merge_with_profile,
)
from density_screener.models import (
    DensityLevel,
    DensitySource,
    OrderBookLevel,
    OrderBookSnapshot,
    ScreenerConfig,
    Side,
)
from density_screener.orderbook import LiveOrderBook, OrderBookOutOfSync
from density_screener.storage import read_json_snapshot, write_json_snapshot


def _snap(last_id: int = 100) -> OrderBookSnapshot:
    return OrderBookSnapshot(
        symbol="BTCUSDT",
        last_update_id=last_id,
        bids=[
            OrderBookLevel(100.0, 1.0),
            OrderBookLevel(99.0, 2.0),
            OrderBookLevel(98.0, 50.0),  # wall
        ],
        asks=[
            OrderBookLevel(101.0, 1.0),
            OrderBookLevel(102.0, 2.0),
            OrderBookLevel(103.0, 50.0),  # wall
        ],
    )


def test_load_snapshot_and_mid():
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap())
    assert book.synced
    assert book.last_update_id == 100
    mid = book.mid_price()
    assert mid is not None
    assert 100.0 < mid < 101.0


def test_apply_diff_and_stale():
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap(100))
    book.apply_diff(101, 101, [(100.0, 1.5)], [])
    assert book.bids[100.0] == 1.5
    book.apply_diff(50, 50, [(100.0, 9.0)], [])  # stale final_id
    assert book.bids[100.0] == 1.5


def test_gap_raises():
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap(100))
    with pytest.raises(OrderBookOutOfSync):
        book.apply_diff(110, 110, [(100.0, 1.0)], [])


def test_remove_level():
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap(100))
    book.apply_diff(101, 101, [(99.0, 0.0)], [])
    assert 99.0 not in book.bids


def test_bucketize():
    levels = [OrderBookLevel(100.0, 1.0), OrderBookLevel(100.4, 2.0), OrderBookLevel(101.0, 3.0)]
    buckets = bucketize(levels, bucket_width=1.0)
    assert buckets[100.0] == 3.0  # 100 + 100.4
    assert buckets[101.0] == 3.0


def test_detector_persistence():
    cfg = ScreenerConfig(
        symbol="BTCUSDT",
        bucket_size_pct=1.0,
        strength_percentile=0.5,
        min_persistence_seconds=0.05,
    )
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap(100))
    det = OrderBookDensityDetector(cfg)
    # first tick — candidates only
    assert det.update(book, "BTCUSDT") == []
    time.sleep(0.06)
    levels = det.update(book, "BTCUSDT")
    assert len(levels) >= 1
    assert all(isinstance(lv, DensityLevel) for lv in levels)


def test_eaten_removes_level():
    cfg = ScreenerConfig(
        symbol="BTCUSDT",
        bucket_size_pct=1.0,
        strength_percentile=0.5,
        min_persistence_seconds=0.0,
        eaten_threshold=0.5,
    )
    book = LiveOrderBook("BTCUSDT")
    book.load_snapshot(_snap(100))
    det = OrderBookDensityDetector(cfg)
    levels = det.update(book, "BTCUSDT")
    assert levels
    # crush the ask wall
    book.apply_diff(101, 101, [], [(103.0, 1.0)])
    levels2 = det.update(book, "BTCUSDT")
    ask_walls = [lv for lv in levels2 if lv.side == Side.ASK and abs(lv.price - 103.5) < 2]
    # wall either gone or marked eaten / weaker — at least not 50 vol
    for lv in ask_walls:
        assert lv.volume < 50


def test_volume_profile_poc():
    cfg = ScreenerConfig(profile_window_seconds=60)
    vp = VolumeProfileBuilder(cfg)
    now = time.time()
    vp.add_trade(100.0, 1.0, now)
    vp.add_trade(100.0, 5.0, now)
    vp.add_trade(110.0, 1.0, now)
    poc = vp.poc(bucket_width=1.0)
    assert poc == 100.0


def test_merge_with_profile_both():
    level = DensityLevel(
        symbol="BTCUSDT",
        price=100.5,
        side=Side.BID,
        volume=10,
        strength=0.9,
        source=DensitySource.ORDERBOOK,
        bucket_low=100.0,
        bucket_high=101.0,
    )
    profile = {100.0: 100.0, 105.0: 1.0}
    merged = merge_with_profile([level], profile, profile_percentile=0.5)
    assert merged[0].source == DensitySource.BOTH


def test_storage_atomic_roundtrip(tmp_path: Path):
    path = tmp_path / "density_snapshot.json"
    level = DensityLevel(
        symbol="BTCUSDT",
        price=100.0,
        side=Side.ASK,
        volume=5,
        strength=0.8,
        source=DensitySource.ORDERBOOK,
        bucket_low=99.5,
        bucket_high=100.5,
    )
    write_json_snapshot(str(path), "BTCUSDT", 99.0, [level])
    data = read_json_snapshot(str(path))
    assert data["symbol"] == "BTCUSDT"
    assert data["current_price"] == 99.0
    assert len(data["levels"]) == 1
    assert data["levels"][0]["distance_pct"] is not None


def test_default_paths():
    from density_screener.paths import default_bridge_dir, default_snapshot_path

    assert default_snapshot_path().name == "density_snapshot.json"
    assert default_bridge_dir().name == "bridge"


def test_market_endpoints():
    from density_screener.markets import MarketKind, endpoints_for

    spot = endpoints_for(MarketKind.SPOT)
    assert "api.binance.com" in spot.rest_base
    fut = endpoints_for(MarketKind.FUTURES_DEMO)
    assert "demo-fapi" in fut.rest_base
    assert "demo-fstream" in fut.ws_base
    assert fut.depth_path.startswith("/fapi/")


def test_bounce_evaluate_near_bid():
    from density_screener.bounce_strategy import BounceConfig, evaluate_snapshot

    snap = {
        "symbol": "BTCUSDT",
        "current_price": 100.0,
        "levels": [
            {
                "price": 99.95,
                "side": "bid",
                "strength": 0.9,
                "eaten_ratio": 0.1,
                "source": "both",
                "distance_pct": -0.05,
            }
        ],
    }
    sig = evaluate_snapshot(snap, BounceConfig(approach_pct=0.1, min_strength=0.5))
    assert sig is not None
    assert sig.side == "BUY"


def test_storage_includes_market(tmp_path: Path):
    path = tmp_path / "density_snapshot.json"
    write_json_snapshot(str(path), "BTCUSDT", 1.0, [], market="futures-demo")
    data = read_json_snapshot(str(path))
    assert data["market"] == "futures-demo"
