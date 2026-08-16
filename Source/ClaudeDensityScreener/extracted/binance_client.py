"""
Binance connectivity: REST snapshot + WS diff-depth + WS aggTrade.

Kept separate from LiveOrderBook / DensityDetector on purpose so the
detection logic can be unit-tested without any network access.

Requires: `pip install websockets aiohttp` (see requirements.txt).
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import AsyncIterator, Callable, Optional

import aiohttp
import websockets

from .models import OrderBookLevel, OrderBookSnapshot
from .orderbook import LiveOrderBook, OrderBookOutOfSync

REST_BASE = "https://api.binance.com"
WS_BASE = "wss://stream.binance.com:9443/ws"

log = logging.getLogger("density_screener.binance")


async def fetch_depth_snapshot(session: aiohttp.ClientSession, symbol: str,
                                limit: int = 1000) -> OrderBookSnapshot:
    url = f"{REST_BASE}/api/v3/depth"
    async with session.get(url, params={"symbol": symbol.upper(), "limit": limit}) as resp:
        resp.raise_for_status()
        data = await resp.json()
    bids = [OrderBookLevel(float(p), float(q)) for p, q in data["bids"]]
    asks = [OrderBookLevel(float(p), float(q)) for p, q in data["asks"]]
    return OrderBookSnapshot(symbol=symbol.upper(), last_update_id=data["lastUpdateId"],
                              bids=bids, asks=asks)


async def stream_depth_diffs(symbol: str) -> AsyncIterator[dict]:
    """Yields raw diff-depth event dicts from the combined depth stream."""
    url = f"{WS_BASE}/{symbol.lower()}@depth@100ms"
    async for ws in websockets.connect(url, ping_interval=20, ping_timeout=20):
        try:
            async for raw in ws:
                yield json.loads(raw)
        except websockets.ConnectionClosed:
            log.warning("depth stream closed, reconnecting...")
            continue


async def stream_agg_trades(symbol: str) -> AsyncIterator[dict]:
    url = f"{WS_BASE}/{symbol.lower()}@aggTrade"
    async for ws in websockets.connect(url, ping_interval=20, ping_timeout=20):
        try:
            async for raw in ws:
                yield json.loads(raw)
        except websockets.ConnectionClosed:
            log.warning("aggTrade stream closed, reconnecting...")
            continue


class OrderBookMaintainer:
    """
    Wires stream_depth_diffs() into a LiveOrderBook, handling the
    Binance resync recipe (buffer -> snapshot -> replay) including
    automatic recovery from OrderBookOutOfSync.
    """

    def __init__(self, symbol: str, depth_limit: int = 1000,
                 on_update: Optional[Callable[[LiveOrderBook], None]] = None):
        self.symbol = symbol.upper()
        self.depth_limit = depth_limit
        self.book = LiveOrderBook(self.symbol)
        self.on_update = on_update
        self._buffer: list[dict] = []

    async def run(self):
        async with aiohttp.ClientSession() as session:
            while True:
                try:
                    await self._resync(session)
                    await self._consume(session)
                except OrderBookOutOfSync as exc:
                    log.warning("orderbook out of sync (%s), resyncing", exc)
                    self.book.synced = False
                    continue

    async def _resync(self, session: aiohttp.ClientSession):
        self._buffer.clear()
        # Start buffering before requesting the snapshot so we don't
        # miss the window between snapshot and first applicable diff.
        buffering_task = asyncio.create_task(self._buffer_diffs())
        await asyncio.sleep(1.0)  # let a few events accumulate
        snapshot = await fetch_depth_snapshot(session, self.symbol, self.depth_limit)
        self.book.load_snapshot(snapshot)
        buffering_task.cancel()

        for event in self._buffer:
            self._apply_event(event)

    async def _buffer_diffs(self):
        async for event in stream_depth_diffs(self.symbol):
            self._buffer.append(event)

    async def _consume(self, session: aiohttp.ClientSession):
        async for event in stream_depth_diffs(self.symbol):
            self._apply_event(event)
            if self.on_update:
                self.on_update(self.book)

    def _apply_event(self, event: dict):
        bid_updates = [(float(p), float(q)) for p, q in event.get("b", [])]
        ask_updates = [(float(p), float(q)) for p, q in event.get("a", [])]
        self.book.apply_diff(event["U"], event["u"], bid_updates, ask_updates)
