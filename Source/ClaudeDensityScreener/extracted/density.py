"""
Core detection logic — pure functions/classes, no network, no I/O.
This is the part covered by unit tests on synthetic data.
"""
from __future__ import annotations

import statistics
from collections import defaultdict
from time import time
from typing import Dict, List, Optional, Tuple

from .models import (DensityLevel, DensitySource, OrderBookLevel, ScreenerConfig, Side)
from .orderbook import LiveOrderBook


def _bucket_key(price: float, bucket_width: float) -> float:
    return round(price / bucket_width) * bucket_width


def bucketize(levels: List[OrderBookLevel], bucket_width: float) -> Dict[float, float]:
    """Aggregate quantity into price buckets. Returns bucket_low_price -> volume."""
    buckets: Dict[float, float] = defaultdict(float)
    for lvl in levels:
        key = _bucket_key(lvl.price, bucket_width)
        buckets[key] += lvl.quantity
    return dict(buckets)


def _percentile_threshold(volumes: List[float], percentile: float) -> float:
    if not volumes:
        return float("inf")
    data = sorted(volumes)
    idx = min(len(data) - 1, int(len(data) * percentile))
    return data[idx]


class OrderBookDensityDetector:
    """
    Detects resting-liquidity walls in the order book.

    Anti-spoofing: a bucket only becomes a confirmed DensityLevel once
    it has stayed above threshold for `min_persistence_seconds`. Until
    then it's tracked internally as a "candidate".
    """

    def __init__(self, config: ScreenerConfig):
        self.config = config
        # bucket_key -> {"side": Side, "first_seen": ts, "peak_volume": float}
        self._candidates: Dict[Tuple[Side, float], dict] = {}
        self._confirmed: Dict[Tuple[Side, float], DensityLevel] = {}

    def update(self, book: LiveOrderBook, symbol: str) -> List[DensityLevel]:
        mid = book.mid_price()
        if mid is None:
            return []
        bucket_width = mid * self.config.bucket_size_pct / 100.0
        now = time()

        bid_buckets = bucketize(book.sorted_bids(), bucket_width)
        ask_buckets = bucketize(book.sorted_asks(), bucket_width)
        threshold = _percentile_threshold(
            list(bid_buckets.values()) + list(ask_buckets.values()),
            self.config.strength_percentile,
        )
        all_vols = list(bid_buckets.values()) + list(ask_buckets.values())
        max_vol = max(all_vols) if all_vols else 1.0

        seen_keys = set()
        for side, buckets in ((Side.BID, bid_buckets), (Side.ASK, ask_buckets)):
            for bucket_price, volume in buckets.items():
                if volume < threshold or threshold == float("inf"):
                    continue
                key = (side, bucket_price)
                seen_keys.add(key)
                self._touch_candidate(key, side, bucket_price, volume, bucket_width,
                                       max_vol, symbol, now)

        self._expire_stale(seen_keys, now)
        return list(self._confirmed.values())

    def _touch_candidate(self, key, side, bucket_price, volume, bucket_width,
                          max_vol, symbol, now):
        cand = self._candidates.get(key)
        if cand is None:
            self._candidates[key] = {
                "first_seen": now, "peak_volume": volume, "last_volume": volume,
            }
            cand = self._candidates[key]
        else:
            cand["peak_volume"] = max(cand["peak_volume"], volume)
            cand["last_volume"] = volume

        age = now - cand["first_seen"]
        eaten_ratio = max(0.0, 1.0 - (cand["last_volume"] / cand["peak_volume"])) \
            if cand["peak_volume"] > 0 else 0.0

        if age >= self.config.min_persistence_seconds:
            level = self._confirmed.get(key)
            price = bucket_price + bucket_width / 2
            strength = min(1.0, volume / max_vol)
            if level is None:
                self._confirmed[key] = DensityLevel(
                    symbol=symbol, price=price, side=side, volume=volume,
                    strength=strength, source=DensitySource.ORDERBOOK,
                    bucket_low=bucket_price, bucket_high=bucket_price + bucket_width,
                    first_seen=cand["first_seen"], last_seen=now, eaten_ratio=eaten_ratio,
                )
            else:
                level.volume = volume
                level.strength = strength
                level.last_seen = now
                level.eaten_ratio = eaten_ratio

    def _expire_stale(self, seen_keys, now):
        for key in list(self._candidates.keys()):
            if key not in seen_keys:
                self._candidates.pop(key, None)
        for key in list(self._confirmed.keys()):
            level = self._confirmed[key]
            if key not in seen_keys or level.eaten_ratio >= self.config.eaten_threshold:
                self._confirmed.pop(key, None)


class VolumeProfileBuilder:
    """
    Builds a rolling volume profile from executed trades (aggTrade
    stream). Harder to spoof than resting orders, used to confirm
    order-book density as DensitySource.BOTH.
    """

    def __init__(self, config: ScreenerConfig):
        self.config = config
        self._trades: List[Tuple[float, float, float]] = []  # (ts, price, qty)

    def add_trade(self, price: float, qty: float, ts: Optional[float] = None):
        ts = ts if ts is not None else time()
        self._trades.append((ts, price, qty))
        self._trim(ts)

    def _trim(self, now: float):
        cutoff = now - self.config.profile_window_seconds
        self._trades = [t for t in self._trades if t[0] >= cutoff]

    def profile(self, bucket_width: float) -> Dict[float, float]:
        buckets: Dict[float, float] = defaultdict(float)
        for _, price, qty in self._trades:
            buckets[_bucket_key(price, bucket_width)] += qty
        return dict(buckets)

    def poc(self, bucket_width: float) -> Optional[float]:
        """Point of Control — the bucket with the most executed volume."""
        prof = self.profile(bucket_width)
        if not prof:
            return None
        return max(prof.items(), key=lambda kv: kv[1])[0]


def merge_with_profile(levels: List[DensityLevel], profile: Dict[float, float],
                        profile_percentile: float = 0.85) -> List[DensityLevel]:
    """
    Upgrades ORDERBOOK-sourced levels to BOTH when the same price
    bucket also shows high executed volume in the trade profile.
    """
    if not profile:
        return levels
    vols = sorted(profile.values())
    threshold = vols[min(len(vols) - 1, int(len(vols) * profile_percentile))]
    hot_buckets = {price for price, vol in profile.items() if vol >= threshold}

    for level in levels:
        if level.bucket_low in hot_buckets or level.bucket_high in hot_buckets:
            level.source = DensitySource.BOTH
    return levels
