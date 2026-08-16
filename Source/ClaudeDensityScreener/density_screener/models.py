"""
Data models used across the density screener.

These are intentionally plain dataclasses with `to_dict`/`from_dict`
helpers so they serialize cleanly to the JSON snapshot consumed by
the WPF UI and by the Hermes agent (file-based IPC).
"""
from __future__ import annotations

from dataclasses import dataclass, field, asdict
from enum import Enum
from time import time
from typing import Optional


class Side(str, Enum):
    BID = "bid"   # support-side liquidity (buy walls) -> bounce UP expected
    ASK = "ask"   # resistance-side liquidity (sell walls) -> bounce DOWN expected


class DensitySource(str, Enum):
    ORDERBOOK = "orderbook"     # resting limit orders (can be spoofed/pulled)
    PROFILE = "profile"         # executed volume (harder to fake)
    BOTH = "both"                # confirmed by both -> strongest signal


@dataclass
class DensityLevel:
    """A single detected liquidity/volume cluster ("плотность")."""

    symbol: str
    price: float
    side: Side
    volume: float                 # aggregated base-asset volume in the bucket
    strength: float                # normalized 0..1 (see density.py for formula)
    source: DensitySource
    bucket_low: float
    bucket_high: float
    first_seen: float = field(default_factory=time)
    last_seen: float = field(default_factory=time)
    eaten_ratio: float = 0.0       # 0 = untouched, 1 = fully consumed since first_seen

    def age_seconds(self, now: Optional[float] = None) -> float:
        return (now or time()) - self.first_seen

    def distance_pct(self, current_price: float) -> float:
        return (self.price - current_price) / current_price * 100.0

    def to_dict(self) -> dict:
        d = asdict(self)
        d["side"] = self.side.value
        d["source"] = self.source.value
        return d

    @staticmethod
    def from_dict(d: dict) -> "DensityLevel":
        d = dict(d)
        d["side"] = Side(d["side"])
        d["source"] = DensitySource(d["source"])
        return DensityLevel(**d)


@dataclass
class OrderBookLevel:
    price: float
    quantity: float


@dataclass
class OrderBookSnapshot:
    symbol: str
    last_update_id: int
    bids: list  # list[OrderBookLevel], sorted desc by price
    asks: list  # list[OrderBookLevel], sorted asc by price
    timestamp: float = field(default_factory=time)


@dataclass
class ScreenerConfig:
    symbol: str = "BTCUSDT"
    market: str = "spot"  # spot | futures-demo
    bucket_size_pct: float = 0.05        # bucket width as % of mid price
    strength_percentile: float = 0.90    # bucket must be >= this percentile of volume dist.
    min_persistence_seconds: float = 3.0  # anti-spoofing: must survive this long
    eaten_threshold: float = 0.6          # ratio of volume drop that marks a level "eaten"
    profile_window_seconds: float = 900.0  # rolling window for volume-profile (trades)
    snapshot_interval_seconds: float = 1.0
    depth_limit: int = 1000
    output_path: str = ""  # empty → %LocalAppData%/HermesDensity/bridge/density_snapshot.json
