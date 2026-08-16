"""
Local live order book, maintained per Binance's official recipe:

1. Buffer incoming diff-depth events from the WebSocket.
2. Fetch a REST snapshot (`/api/v3/depth`).
3. Discard buffered events where `u` <= snapshot.lastUpdateId.
4. Apply the first remaining event only if `U <= lastUpdateId+1 <= u`.
5. Apply all subsequent events in order; each event's `U` must equal
   the previous event's `u + 1`, otherwise the book is out of sync
   and must be rebuilt from a fresh snapshot.

This module has no network code (see binance_client.py) — it only
knows how to apply snapshots and diffs to an in-memory book, which
keeps it trivially unit-testable with synthetic data.
"""
from __future__ import annotations

from time import time
from typing import Dict, List, Optional

from .models import OrderBookLevel, OrderBookSnapshot


class OrderBookOutOfSync(Exception):
    """Raised when a diff event can't be applied cleanly; caller must resync."""


class LiveOrderBook:
    def __init__(self, symbol: str):
        self.symbol = symbol
        self.last_update_id: int = -1
        self.bids: Dict[float, float] = {}   # price -> quantity
        self.asks: Dict[float, float] = {}
        self.synced = False
        self.updated_at: float = 0.0

    # ---- lifecycle -------------------------------------------------

    def load_snapshot(self, snapshot: OrderBookSnapshot) -> None:
        self.bids = {lvl.price: lvl.quantity for lvl in snapshot.bids if lvl.quantity > 0}
        self.asks = {lvl.price: lvl.quantity for lvl in snapshot.asks if lvl.quantity > 0}
        self.last_update_id = snapshot.last_update_id
        self.synced = True
        self.updated_at = snapshot.timestamp

    def apply_diff(self, first_update_id: int, final_update_id: int,
                    bid_updates: List[tuple], ask_updates: List[tuple]) -> None:
        """
        bid_updates / ask_updates: list of (price, quantity) tuples,
        quantity == 0 means "remove this price level".
        """
        if not self.synced:
            raise OrderBookOutOfSync("book has no snapshot loaded yet")

        # First diff after snapshot must straddle last_update_id.
        if final_update_id <= self.last_update_id:
            return  # stale event, ignore

        if self.last_update_id >= 0 and first_update_id > self.last_update_id + 1:
            raise OrderBookOutOfSync(
                f"gap detected: expected U<={self.last_update_id + 1}, got U={first_update_id}"
            )

        for price, qty in bid_updates:
            if qty == 0:
                self.bids.pop(price, None)
            else:
                self.bids[price] = qty

        for price, qty in ask_updates:
            if qty == 0:
                self.asks.pop(price, None)
            else:
                self.asks[price] = qty

        self.last_update_id = final_update_id
        self.updated_at = time()

    # ---- accessors ---------------------------------------------------

    def mid_price(self) -> Optional[float]:
        if not self.bids or not self.asks:
            return None
        return (max(self.bids) + min(self.asks)) / 2.0

    def sorted_bids(self) -> List[OrderBookLevel]:
        return [OrderBookLevel(p, q) for p, q in sorted(self.bids.items(), key=lambda x: -x[0])]

    def sorted_asks(self) -> List[OrderBookLevel]:
        return [OrderBookLevel(p, q) for p, q in sorted(self.asks.items(), key=lambda x: x[0])]

    def depth_within_pct(self, pct: float) -> tuple:
        """Return (bids, asks) restricted to +/- pct of mid price."""
        mid = self.mid_price()
        if mid is None:
            return [], []
        lo, hi = mid * (1 - pct / 100.0), mid * (1 + pct / 100.0)
        bids = [lvl for lvl in self.sorted_bids() if lvl.price >= lo]
        asks = [lvl for lvl in self.sorted_asks() if lvl.price <= hi]
        return bids, asks
