"""
Binance connectivity: REST snapshot + WS diff-depth + WS aggTrade.

Supports Spot and Futures Demo via MarketEndpoints.
OrderBookMaintainer keeps a **single** depth WebSocket across resyncs so
update IDs stay continuous (reconnecting after every gap was dropping events).
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import AsyncIterator, Callable, Optional

import aiohttp
import websockets

from .markets import MarketEndpoints, MarketKind, endpoints_for
from .models import OrderBookLevel, OrderBookSnapshot
from .orderbook import LiveOrderBook, OrderBookOutOfSync

log = logging.getLogger("density_screener.binance")


async def fetch_depth_snapshot(
    session: aiohttp.ClientSession,
    symbol: str,
    endpoints: MarketEndpoints,
    limit: int = 1000,
) -> OrderBookSnapshot:
    async with session.get(
        endpoints.depth_url, params={"symbol": symbol.upper(), "limit": limit}
    ) as resp:
        resp.raise_for_status()
        data = await resp.json()
    bids = [OrderBookLevel(float(p), float(q)) for p, q in data["bids"]]
    asks = [OrderBookLevel(float(p), float(q)) for p, q in data["asks"]]
    return OrderBookSnapshot(
        symbol=symbol.upper(),
        last_update_id=data["lastUpdateId"],
        bids=bids,
        asks=asks,
    )


async def stream_depth_diffs(symbol: str, endpoints: MarketEndpoints) -> AsyncIterator[dict]:
    url = endpoints.depth_stream_url(symbol)
    while True:
        try:
            async with websockets.connect(url, ping_interval=20, ping_timeout=20) as ws:
                async for raw in ws:
                    yield json.loads(raw)
        except Exception as exc:
            log.warning("depth stream error (%s), reconnecting in 1s…", exc)
            await asyncio.sleep(1.0)


async def stream_agg_trades(symbol: str, endpoints: MarketEndpoints) -> AsyncIterator[dict]:
    url = endpoints.agg_trade_stream_url(symbol)
    while True:
        try:
            async with websockets.connect(url, ping_interval=20, ping_timeout=20) as ws:
                async for raw in ws:
                    yield json.loads(raw)
        except Exception as exc:
            log.warning("aggTrade stream error (%s), reconnecting in 1s…", exc)
            await asyncio.sleep(1.0)


class OrderBookMaintainer:
    """Continuous depth WS + REST snapshot resync (Binance recipe)."""

    def __init__(
        self,
        symbol: str,
        depth_limit: int = 1000,
        market: MarketKind | str = MarketKind.SPOT,
        on_update: Optional[Callable[[LiveOrderBook], None]] = None,
    ):
        self.symbol = symbol.upper()
        self.depth_limit = depth_limit
        self.endpoints = endpoints_for(market)
        self.book = LiveOrderBook(self.symbol)
        self.on_update = on_update
        self._buffer: list[dict] = []
        self._need_resync = True
        self._lock = asyncio.Lock()

    async def run(self):
        async with aiohttp.ClientSession() as session:
            await asyncio.gather(
                self._depth_loop(session),
                self._resync_watchdog(session),
            )

    async def _resync_watchdog(self, session: aiohttp.ClientSession):
        while True:
            if self._need_resync:
                async with self._lock:
                    if self._need_resync:
                        await self._do_resync(session)
                        self._need_resync = False
            await asyncio.sleep(0.2)

    async def _do_resync(self, session: aiohttp.ClientSession):
        self.book.synced = False
        await asyncio.sleep(0.5)
        snapshot = await fetch_depth_snapshot(
            session, self.symbol, self.endpoints, self.depth_limit
        )
        self.book.load_snapshot(snapshot)
        buffered = list(self._buffer)
        self._buffer.clear()
        applied = 0
        for event in buffered:
            try:
                self._apply_event(event)
                applied += 1
            except OrderBookOutOfSync:
                continue
        log.info(
            "resync ok market=%s lastUpdateId=%s buffered=%d applied=%d",
            self.endpoints.kind.value,
            snapshot.last_update_id,
            len(buffered),
            applied,
        )

    async def _depth_loop(self, session: aiohttp.ClientSession):
        self._need_resync = True
        async for event in stream_depth_diffs(self.symbol, self.endpoints):
            if self._need_resync or not self.book.synced:
                self._buffer.append(event)
                if len(self._buffer) > 5000:
                    self._buffer = self._buffer[-2000:]
                continue
            try:
                self._apply_event(event)
                if self.on_update:
                    self.on_update(self.book)
            except OrderBookOutOfSync as exc:
                log.warning("orderbook out of sync (%s), scheduling resync", exc)
                self._buffer.append(event)
                self._need_resync = True

    def _apply_event(self, event: dict):
        bid_updates = [(float(p), float(q)) for p, q in event.get("b", [])]
        ask_updates = [(float(p), float(q)) for p, q in event.get("a", [])]
        first_id = int(event.get("U", 0))
        final_id = int(event.get("u", 0))
        prev_u = event.get("pu")
        if prev_u is not None and self.book.synced and self.book.last_update_id >= 0:
            if int(prev_u) != self.book.last_update_id and final_id > self.book.last_update_id:
                if first_id > self.book.last_update_id + 1:
                    raise OrderBookOutOfSync(
                        f"pu mismatch: have {self.book.last_update_id}, event pu={prev_u} U={first_id}"
                    )
        self.book.apply_diff(first_id, final_id, bid_updates, ask_updates)
