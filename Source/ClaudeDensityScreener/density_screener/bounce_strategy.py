"""
Bounce-off-density strategy (phase F).

Reads HermesDensity snapshot; when price approaches a wall with slowing
impulse filters (strength + not eaten), proposes / enqueues place_order
via HermesFutures bridge. Risk sizing stays in the Futures terminal
(CapNotional + ValidateOrder).

Default is dry-run. Live requires --live and a running Futures Demo terminal.
"""
from __future__ import annotations

import argparse
import logging
import time
from dataclasses import dataclass
from typing import Any, Optional

from .futures_bridge import enqueue_place_order, futures_terminal_alive
from .paths import default_snapshot_path
from .storage import read_json_snapshot

log = logging.getLogger("density_screener.bounce")


@dataclass
class BounceConfig:
    approach_pct: float = 0.08  # |distance_pct| below this → near wall
    min_strength: float = 0.55
    max_eaten: float = 0.45
    prefer_both: bool = True
    quantity_usdt: float = 25.0
    cooldown_sec: float = 120.0
    poll_sec: float = 2.0
    dry_run: bool = True
    symbol_filter: str = ""


@dataclass
class BounceSignal:
    symbol: str
    side: str  # BUY into bid wall, SELL into ask wall
    level_price: float
    strength: float
    distance_pct: float
    source: str
    reason: str


def evaluate_snapshot(snap: dict[str, Any], cfg: BounceConfig) -> Optional[BounceSignal]:
    price = snap.get("current_price")
    if price is None or price <= 0:
        return None
    symbol = str(snap.get("symbol") or "").upper()
    if cfg.symbol_filter and symbol != cfg.symbol_filter.upper():
        return None

    levels = list(snap.get("levels") or [])
    candidates: list[BounceSignal] = []
    for lvl in levels:
        strength = float(lvl.get("strength") or 0)
        eaten = float(lvl.get("eaten_ratio") or 0)
        source = str(lvl.get("source") or "")
        dist = lvl.get("distance_pct")
        if dist is None:
            continue
        dist = float(dist)
        if strength < cfg.min_strength or eaten > cfg.max_eaten:
            continue
        if cfg.prefer_both and source != "both" and strength < 0.75:
            continue
        side_wall = str(lvl.get("side") or "")
        # Bid wall below → expect bounce up → BUY; ask wall above → SELL
        if side_wall == "bid" and -cfg.approach_pct <= dist <= 0:
            candidates.append(
                BounceSignal(
                    symbol=symbol,
                    side="BUY",
                    level_price=float(lvl["price"]),
                    strength=strength,
                    distance_pct=dist,
                    source=source,
                    reason=f"near bid wall strength={strength:.2f} eaten={eaten:.2f}",
                )
            )
        elif side_wall == "ask" and 0 <= dist <= cfg.approach_pct:
            candidates.append(
                BounceSignal(
                    symbol=symbol,
                    side="SELL",
                    level_price=float(lvl["price"]),
                    strength=strength,
                    distance_pct=dist,
                    source=source,
                    reason=f"near ask wall strength={strength:.2f} eaten={eaten:.2f}",
                )
            )

    if not candidates:
        return None
    candidates.sort(key=lambda s: (-s.strength, abs(s.distance_pct)))
    return candidates[0]


def run_loop(cfg: BounceConfig, snapshot_path: str) -> None:
    last_fire = 0.0
    log.info(
        "bounce strategy watching %s dry_run=%s approach=%.3f%% qty=%.2f",
        snapshot_path,
        cfg.dry_run,
        cfg.approach_pct,
        cfg.quantity_usdt,
    )
    while True:
        try:
            if not os_path_exists(snapshot_path):
                log.warning("snapshot missing: %s", snapshot_path)
                time.sleep(cfg.poll_sec)
                continue
            snap = read_json_snapshot(snapshot_path)
            signal = evaluate_snapshot(snap, cfg)
            now = time.time()
            if signal and (now - last_fire) >= cfg.cooldown_sec:
                log.info(
                    "SIGNAL %s %s @~%.2f dist=%.4f%% %s",
                    signal.side,
                    signal.symbol,
                    signal.level_price,
                    signal.distance_pct,
                    signal.reason,
                )
                if not cfg.dry_run and not futures_terminal_alive():
                    log.error("skip live: Futures terminal not alive")
                else:
                    result = enqueue_place_order(
                        symbol=signal.symbol,
                        side=signal.side,
                        quantity_usdt=cfg.quantity_usdt,
                        dry_run=cfg.dry_run,
                    )
                    log.info("enqueue: %s", result)
                    last_fire = now
            else:
                mid = snap.get("current_price")
                n = len(snap.get("levels") or [])
                log.debug("idle mid=%s levels=%d", mid, n)
        except Exception as exc:
            log.exception("bounce loop error: %s", exc)
        time.sleep(cfg.poll_sec)


def os_path_exists(path: str) -> bool:
    import os

    return os.path.isfile(path)


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    p = argparse.ArgumentParser(description="Density bounce → Futures bridge")
    p.add_argument("--snapshot", default=str(default_snapshot_path()))
    p.add_argument("--approach-pct", type=float, default=0.08)
    p.add_argument("--min-strength", type=float, default=0.55)
    p.add_argument("--quantity-usdt", type=float, default=25.0)
    p.add_argument("--cooldown-sec", type=float, default=120.0)
    p.add_argument("--poll-sec", type=float, default=2.0)
    p.add_argument("--symbol", default="", help="Optional filter, e.g. BTCUSDT")
    p.add_argument(
        "--live",
        action="store_true",
        help="Actually enqueue orders (default is dry-run)",
    )
    args = p.parse_args()
    cfg = BounceConfig(
        approach_pct=args.approach_pct,
        min_strength=args.min_strength,
        quantity_usdt=args.quantity_usdt,
        cooldown_sec=args.cooldown_sec,
        poll_sec=args.poll_sec,
        dry_run=not args.live,
        symbol_filter=args.symbol,
    )
    if args.live:
        log.warning("LIVE MODE: orders go to HermesFutures/commands.json (terminal must run)")
    run_loop(cfg, args.snapshot)


if __name__ == "__main__":
    main()
